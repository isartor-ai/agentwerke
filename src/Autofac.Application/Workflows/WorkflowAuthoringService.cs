using Autofac.Domain.Persistence;

namespace Autofac.Application.Workflows;

public sealed class WorkflowAuthoringService : IWorkflowAuthoringService
{
    private const string DraftStatus = "draft";
    private const string ActiveStatus = "active";
    private const string ValidState = "valid";
    private const string InvalidState = "invalid";
    private const string SystemOwner = "system";

    private readonly IWorkflowDefinitionRepository _repository;
    private readonly IWorkflowValidationService _validationService;

    public WorkflowAuthoringService(
        IWorkflowDefinitionRepository repository,
        IWorkflowValidationService validationService)
    {
        _repository = repository;
        _validationService = validationService;
    }

    public Task<IReadOnlyList<WorkflowDefinition>> ListWorkflowsAsync(CancellationToken cancellationToken = default)
        => _repository.ListAsync(cancellationToken);

    public Task<WorkflowDefinition?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
        => _repository.GetAsync(workflowId, cancellationToken);

    public async Task<WorkflowImportResult> ImportWorkflowAsync(
        ImportWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = ValidateWorkflow(command.BpmnXml);
        var now = DateTimeOffset.UtcNow.ToString("o");
        var workflow = new WorkflowDefinition
        {
            Id = $"wf_{Guid.NewGuid():N}",
            Name = validation.ProcessName ?? command.FileName,
            Description = command.Description ?? string.Empty,
            Version = WorkflowVersioning.InitialVersion,
            Status = DraftStatus,
            Owner = string.IsNullOrWhiteSpace(command.Owner) ? SystemOwner : command.Owner,
            CreatedAt = now,
            LastEditedAt = now,
            ValidationState = validation.IsValid ? ValidState : InvalidState,
            BpmnXml = command.BpmnXml
        };

        await _repository.AddAsync(workflow, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new WorkflowImportResult(workflow.Id, validation);
    }

    public WorkflowValidationResult ValidateWorkflow(string bpmnXml)
        => _validationService.Validate(bpmnXml);

    public async Task<WorkflowPublishResult> PublishWorkflowAsync(
        string workflowId,
        PublishWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentNullException.ThrowIfNull(command);

        var validation = ValidateWorkflow(command.BpmnXml);
        if (!validation.IsValid)
        {
            throw new WorkflowValidationException(validation);
        }

        var workflow = await _repository.FindTrackedAsync(workflowId, cancellationToken);
        if (workflow is null)
        {
            throw new WorkflowNotFoundException(workflowId);
        }

        var now = DateTimeOffset.UtcNow.ToString("o");
        workflow.Name = validation.ProcessName ?? workflow.Name;
        workflow.Description = command.Description ?? workflow.Description;
        workflow.Status = ActiveStatus;
        workflow.LastEditedAt = now;
        workflow.ValidationState = ValidState;
        workflow.BpmnXml = command.BpmnXml;
        workflow.Version = WorkflowVersioning.NextPublishedVersion(workflow.Version);

        await _repository.SaveChangesAsync(cancellationToken);

        return new WorkflowPublishResult(workflow.Id, workflow.Version, now);
    }
}

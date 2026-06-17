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
    private readonly IWorkflowDeploymentService _deploymentService;

    public WorkflowAuthoringService(
        IWorkflowDefinitionRepository repository,
        IWorkflowValidationService validationService,
        IWorkflowDeploymentService deploymentService)
    {
        _repository = repository;
        _validationService = validationService;
        _deploymentService = deploymentService;
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

        var preparation = _validationService.PrepareForPublish(command.BpmnXml);
        if (!preparation.IsValid)
        {
            throw new WorkflowValidationException(preparation.Validation);
        }

        var workflow = await _repository.FindTrackedAsync(workflowId, cancellationToken);
        if (workflow is null)
        {
            throw new WorkflowNotFoundException(workflowId);
        }

        var nextVersion = WorkflowVersioning.NextPublishedVersion(workflow.Version);
        var deployment = await _deploymentService.DeployAsync(
            new WorkflowDeploymentRequest(
                WorkflowId: workflow.Id,
                Version: nextVersion,
                ProcessDefinitionId: preparation.Validation.ProcessId ?? workflow.Id,
                ProjectedBpmnXml: preparation.ProjectedBpmnXml!,
                ResourceName: BuildDeploymentResourceName(preparation.Validation.ProcessId, workflow.Id)),
            cancellationToken);

        var now = DateTimeOffset.UtcNow.ToString("o");
        workflow.Name = preparation.Validation.ProcessName ?? workflow.Name;
        workflow.Description = command.Description ?? workflow.Description;
        workflow.Status = ActiveStatus;
        workflow.LastEditedAt = now;
        workflow.ValidationState = ValidState;
        workflow.BpmnXml = command.BpmnXml;
        workflow.Version = nextVersion;
        workflow.CamundaDeploymentKey = deployment.DeploymentKey;
        workflow.CamundaProcessDefinitionId = deployment.ProcessDefinitionId;
        workflow.CamundaProcessDefinitionKey = deployment.ProcessDefinitionKey;
        workflow.CamundaProcessDefinitionVersion = deployment.ProcessDefinitionVersion;
        workflow.CamundaDeployedAt = deployment.DeployedAt;

        await _repository.SaveChangesAsync(cancellationToken);

        return new WorkflowPublishResult(workflow.Id, workflow.Version, now, deployment);
    }

    private static string BuildDeploymentResourceName(string? processDefinitionId, string workflowId)
    {
        var baseName = string.IsNullOrWhiteSpace(processDefinitionId)
            ? workflowId
            : processDefinitionId.Trim();

        return $"{baseName}.bpmn";
    }
}

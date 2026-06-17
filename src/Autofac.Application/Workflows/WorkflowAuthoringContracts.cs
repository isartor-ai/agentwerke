using Autofac.Domain.Persistence;

namespace Autofac.Application.Workflows;

public sealed record ImportWorkflowCommand(
    string FileName,
    string BpmnXml,
    string? Description = null,
    string? Owner = null);

public sealed record PublishWorkflowCommand(
    string BpmnXml,
    string? Description = null);

public sealed record WorkflowImportResult(
    string WorkflowId,
    WorkflowValidationResult Validation);

public sealed record WorkflowPublishResult(
    string WorkflowId,
    string Version,
    string PublishedAt,
    WorkflowDeploymentResult Camunda);

public sealed record WorkflowValidationError(
    string Message,
    string? ElementId,
    string ElementName,
    int? LineNumber,
    int? LinePosition);

public sealed record WorkflowValidationWarning(
    string Message,
    string? ElementId,
    string ElementName,
    int? LineNumber,
    int? LinePosition);

public sealed record WorkflowValidationResult(
    bool IsValid,
    string? ProcessId,
    string? ProcessName,
    IReadOnlyList<WorkflowValidationError> Errors,
    IReadOnlyList<WorkflowValidationWarning> Warnings);

public sealed record WorkflowPublishPreparation(
    WorkflowValidationResult Validation,
    string? ProjectedBpmnXml)
{
    public bool IsValid => Validation.IsValid && !string.IsNullOrWhiteSpace(ProjectedBpmnXml);
}

public sealed record WorkflowDeploymentRequest(
    string WorkflowId,
    string Version,
    string ProcessDefinitionId,
    string ProjectedBpmnXml,
    string ResourceName);

public sealed record WorkflowDeploymentResult(
    string DeploymentKey,
    string ProcessDefinitionId,
    string ProcessDefinitionKey,
    int ProcessDefinitionVersion,
    string DeployedAt);

public sealed record WorkflowDeploymentError(
    string Code,
    string Message);

public interface IWorkflowAuthoringService
{
    Task<IReadOnlyList<WorkflowDefinition>> ListWorkflowsAsync(CancellationToken cancellationToken = default);

    Task<WorkflowDefinition?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);

    Task<WorkflowImportResult> ImportWorkflowAsync(
        ImportWorkflowCommand command,
        CancellationToken cancellationToken = default);

    WorkflowValidationResult ValidateWorkflow(string bpmnXml);

    Task<WorkflowPublishResult> PublishWorkflowAsync(
        string workflowId,
        PublishWorkflowCommand command,
        CancellationToken cancellationToken = default);
}

public interface IWorkflowDefinitionRepository
{
    Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken cancellationToken = default);

    Task<WorkflowDefinition?> GetAsync(string workflowId, CancellationToken cancellationToken = default);

    Task<WorkflowDefinition?> FindTrackedAsync(string workflowId, CancellationToken cancellationToken = default);

    Task AddAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IWorkflowValidationService
{
    WorkflowValidationResult Validate(string bpmnXml);

    WorkflowPublishPreparation PrepareForPublish(string bpmnXml);
}

public interface IWorkflowDeploymentService
{
    Task<WorkflowDeploymentResult> DeployAsync(
        WorkflowDeploymentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowNotFoundException : Exception
{
    public WorkflowNotFoundException(string workflowId)
        : base($"Workflow '{workflowId}' was not found.")
    {
        WorkflowId = workflowId;
    }

    public string WorkflowId { get; }
}

public sealed class WorkflowValidationException : Exception
{
    public WorkflowValidationException(WorkflowValidationResult validation)
        : base("Workflow validation failed. Fix errors before publishing.")
    {
        Validation = validation;
    }

    public WorkflowValidationResult Validation { get; }
}

public sealed class WorkflowDeploymentException : Exception
{
    public WorkflowDeploymentException(
        string message,
        IReadOnlyList<WorkflowDeploymentError> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyList<WorkflowDeploymentError> Errors { get; }
}

public static class WorkflowVersioning
{
    public const string InitialVersion = "v1.0.0";

    public static string NextPublishedVersion(string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return InitialVersion;
        }

        var versionText = currentVersion.Trim();
        if (!versionText.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            return InitialVersion;
        }

        var segments = versionText[1..].Split('.');
        if (segments.Length != 3 || !int.TryParse(segments[0], out var major))
        {
            return InitialVersion;
        }

        return $"v{major + 1}.0.0";
    }
}

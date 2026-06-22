using Autofac.Workflows.Bpmn;

namespace Autofac.Workflows.Runtime;

public interface IWorkflowEngineAdapter
{
    string EngineId { get; }

    Task<WorkflowExecutionState> StartAsync(
        WorkflowEngineStartRequest request,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionState> ResumeAsync(
        WorkflowEngineResumeRequest request,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionState> RecoverAsync(
        WorkflowEngineRecoverRequest request,
        CancellationToken cancellationToken);
}

public sealed record WorkflowEngineStartRequest(
    string WorkflowDefinitionId,
    BpmnWorkflowDefinition Definition,
    string? Initiator,
    string? CorrelationId = null,
    string? ExistingRunId = null);

public sealed record WorkflowEngineResumeRequest(
    string RunId,
    BpmnWorkflowDefinition Definition,
    string? ApprovedBy,
    string? ExternalCorrelationKey = null,
    IReadOnlyDictionary<string, string>? ExternalPayload = null,
    string? ResumedBy = null);

public sealed record WorkflowEngineRecoverRequest(
    string RunId,
    BpmnWorkflowDefinition Definition);

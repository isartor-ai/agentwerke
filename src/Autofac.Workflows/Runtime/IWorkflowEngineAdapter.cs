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
    string? CorrelationId = null);

public sealed record WorkflowEngineResumeRequest(
    string RunId,
    BpmnWorkflowDefinition Definition,
    string? ApprovedBy);

public sealed record WorkflowEngineRecoverRequest(
    string RunId,
    BpmnWorkflowDefinition Definition);

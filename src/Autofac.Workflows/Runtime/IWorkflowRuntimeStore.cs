using Autofac.Domain.AgentRuntime;
using Autofac.Domain.Persistence;

namespace Autofac.Workflows.Runtime;

public interface IWorkflowRuntimeStore
{
    Task<WorkflowRun> CreateRunAsync(
        string workflowDefinitionId,
        string? initiator,
        CancellationToken cancellationToken,
        string? correlationId = null);

    Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowEvent>> ListRunEventsAsync(string runId, CancellationToken cancellationToken);

    Task AppendEventAsync(
        string runId,
        string type,
        string message,
        CancellationToken cancellationToken);

    Task UpdateRunStatusAsync(
        string runId,
        string status,
        string? completedAt,
        CancellationToken cancellationToken);

    Task<WorkflowRunStep> CreateStepAsync(
        string runId,
        string nodeId,
        string? nodeName,
        string nodeType,
        string? agentName,
        CancellationToken cancellationToken);

    Task UpdateStepStatusAsync(
        string stepId,
        string status,
        string? output,
        string? error,
        string? completedAt,
        PolicyDecision? policyDecision,
        AgentRuntimeSnapshot? runtimeSnapshot,
        CancellationToken cancellationToken);
}

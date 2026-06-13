using Autofac.Domain.Persistence;

namespace Autofac.Workflows.Runtime;

public interface IWorkflowRuntimeStore
{
    Task<WorkflowRun> CreateRunAsync(
        string workflowDefinitionId,
        string? initiator,
        CancellationToken cancellationToken);

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
}
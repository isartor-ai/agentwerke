using Autofac.Domain.Persistence;

namespace Autofac.Workflows.Runtime;

public interface IWorkflowRuntimeStore
{
    Task<WorkflowRun> CreateRunAsync(
        Guid workflowDefinitionId,
        string? initiator,
        CancellationToken cancellationToken);

    Task<WorkflowRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowEvent>> ListRunEventsAsync(Guid runId, CancellationToken cancellationToken);

    Task AppendEventAsync(
        Guid runId,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken);

    Task UpdateRunStatusAsync(
        Guid runId,
        string status,
        DateTimeOffset? completedAtUtc,
        CancellationToken cancellationToken);
}
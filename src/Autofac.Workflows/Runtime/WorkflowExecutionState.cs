namespace Autofac.Workflows.Runtime;

public sealed record WorkflowExecutionState(
    string RunId,
    string Status,
    string? NextNodeId,
    string? WaitingOnNodeId,
    string? CompletedAt,
    DateTimeOffset? TimerDueAt = null);

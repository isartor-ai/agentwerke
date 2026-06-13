namespace Autofac.Workflows.Runtime;

public sealed record WorkflowExecutionState(
    string RunId,
    string Status,
    int NextNodeIndex,
    string? WaitingOnNodeId,
    string? CompletedAt);
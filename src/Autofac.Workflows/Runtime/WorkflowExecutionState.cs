namespace Autofac.Workflows.Runtime;

public sealed record WorkflowExecutionState(
    Guid RunId,
    string Status,
    int NextNodeIndex,
    string? WaitingOnNodeId,
    DateTimeOffset? CompletedAtUtc);
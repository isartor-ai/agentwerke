namespace Autofac.Workflows.Runtime;

public sealed record WorkflowExecutionState(
    string RunId,
    string Status,
    string? NextNodeId,
    string? WaitingOnNodeId,
    string? CompletedAt,
    DateTimeOffset? TimerDueAt = null,
    /// <summary>
    /// Name of the artifact the nearest preceding service task produced, when
    /// <see cref="WaitingOnNodeId"/> is a userTask approval gate. Lets the approval card
    /// render the agent's output for human review instead of just policy rationale text.
    /// </summary>
    string? WaitingApprovalArtifactName = null);

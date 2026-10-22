using Agentwerke.Application.Workflows;

namespace Agentwerke.Workflows.Runtime;

public sealed record WorkflowExecutionState(
    string RunId,
    string Status,
    string? NextNodeId,
    string? WaitingOnNodeId,
    string? CompletedAt,
    DateTimeOffset? TimerDueAt = null,
    string? WaitingExternalCorrelationKey = null,
    string? WaitingExternalMessageName = null,
    /// <summary>
    /// Name of the artifact the nearest preceding service task produced, when
    /// <see cref="WaitingOnNodeId"/> is a userTask approval gate. Lets the approval card
    /// render the agent's output for human review instead of just policy rationale text.
    /// </summary>
    string? WaitingApprovalArtifactName = null,
    /// <summary>
    /// All open external waits for this run (#209). For a single sequential wait this has one
    /// element matching <see cref="WaitingOnNodeId"/> / <see cref="WaitingExternalCorrelationKey"/> /
    /// <see cref="WaitingExternalMessageName"/>; for a parallel gateway fan-out across multiple
    /// external waits it contains one slot per still-waiting branch.
    /// </summary>
    IReadOnlyList<WaitingExternalSlot>? WaitingExternalCorrelations = null);

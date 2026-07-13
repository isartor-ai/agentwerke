namespace Agentwerke.Api.Contracts.Approvals;

/// <summary>
/// A pending tool-access escalation (#202) for the Approvals dashboard: an agent asked for a
/// tool its step does not allow, and the run is suspended until an operator decides.
/// Answered via POST /api/runs/{runId}/interactions/{interactionId}/answer.
/// </summary>
public sealed record ToolAccessRequestSummary(
    string InteractionId,
    string RunId,
    string? WorkflowName,
    string? StepId,
    string? StepName,
    string Agent,
    string? ToolName,
    string? Intent,
    string Prompt,
    IReadOnlyList<string> Options,
    string CreatedAt);

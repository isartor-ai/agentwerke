namespace Agentwerke.Domain.Persistence;

/// <summary>
/// A normalized external lifecycle event parsed from a webhook — e.g. a GitHub pull
/// request merge or a workflow_run/check_suite completion. Recorded for observability
/// today (#136); a future run-resume mechanism (#138) will consume these to wake a run
/// paused on a "waiting_external" BPMN node by matching <see cref="CorrelationHint"/>.
/// </summary>
public sealed class ExternalWorkflowEvent
{
    public string Id { get; set; } = string.Empty;

    /// <summary>e.g. "github.pull_request.closed", "github.workflow_run.completed".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Best-effort correlation hint for matching against a waiting run — typically the
    /// head branch name, falling back to the head commit sha when no branch is known.
    /// </summary>
    public string CorrelationHint { get; set; } = string.Empty;

    /// <summary>Normalized payload fields, JSON-serialized.</summary>
    public string Payload { get; set; } = string.Empty;

    public string ReceivedAt { get; set; } = string.Empty;
}

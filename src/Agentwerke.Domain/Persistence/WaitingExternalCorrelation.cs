namespace Agentwerke.Domain.Persistence;

/// <summary>
/// Tracks the single external event a run is currently paused on (status "waiting_external"),
/// so an inbound webhook can be matched back to the waiting run without scanning every run's
/// checkpoint history. One row per run; upserted when a run enters waiting_external, removed
/// once it leaves that state (resumed, failed, or otherwise advanced).
/// </summary>
public sealed class WaitingExternalCorrelation
{
    public string RunId { get; set; } = string.Empty;

    /// <summary>Rendered correlation key the waiting node is keyed on, e.g. a branch name.</summary>
    public string CorrelationKey { get; set; } = string.Empty;

    /// <summary>The BPMN node's configured message name, e.g. "github.pull_request.merged".</summary>
    public string MessageName { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
}

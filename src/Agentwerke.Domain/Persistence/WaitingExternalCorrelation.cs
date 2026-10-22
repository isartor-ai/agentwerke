namespace Agentwerke.Domain.Persistence;

/// <summary>
/// Tracks an external event a run node is currently paused on (status "waiting_external"),
/// so an inbound webhook can be matched back to the waiting run without scanning every run's
/// checkpoint history. Keyed by (RunId, NodeId) so a parallel-gateway workflow can hold multiple
/// concurrent external waits (#209); upserted when a node enters waiting_external and removed
/// once that node resumes or the run leaves waiting_external.
/// </summary>
public sealed class WaitingExternalCorrelation
{
    public string RunId { get; set; } = string.Empty;

    /// <summary>The BPMN node ID of the waiting catch event or receive task (#209).</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Rendered correlation key the waiting node is keyed on, e.g. a branch name.</summary>
    public string CorrelationKey { get; set; } = string.Empty;

    /// <summary>The BPMN node's configured message name, e.g. "github.pull_request.merged".</summary>
    public string MessageName { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
}

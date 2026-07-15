using Agentwerke.Domain.Persistence;

namespace Agentwerke.Application.Agents;

/// <summary>
/// Configuration for interaction delivery (#220).
///
/// Lives in Agentwerke.Application, not alongside IntegrationOptions in Agentwerke.Integrations,
/// because the resolver and router are Application types and Application must not reference
/// Integrations. It still binds the "Integrations:Interactions" config section, so operators see one
/// coherent integrations block.
/// </summary>
public sealed class InteractionOptions
{
    public const string Section = "Integrations:Interactions";

    /// <summary>
    /// The epic's feature flag. Off by default: with it off the resolver returns the UI only, which
    /// reproduces today's behaviour exactly.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Channels used when an interaction requests none. The UI is always added regardless.</summary>
    public List<string> DefaultChannels { get; set; } = [InteractionChannels.Ui];

    /// <summary>Per-workflow overrides, keyed by workflow name. Beaten only by a per-interaction request.</summary>
    public Dictionary<string, List<string>> ChannelsByWorkflow { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-agent overrides, keyed by the agent that raised the interaction.</summary>
    public Dictionary<string, List<string>> ChannelsByAgent { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default timeout for blocking interactions. Null means never expires — the deliberate default,
    /// so enabling this feature cannot start expiring runs that previously waited indefinitely (#221).
    /// </summary>
    public int? DefaultTimeoutSeconds { get; set; }

    /// <summary>How often the timeout sweeper checks for due interactions.</summary>
    public int SweepIntervalSeconds { get; set; } = 30;

    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>Base delay for exponential backoff between delivery attempts.</summary>
    public int RetryBaseDelayMs { get; set; } = 200;

    /// <summary>Public base URL a channel points a responder at, e.g. https://agentwerke.example.</summary>
    public string RespondUrlBase { get; set; } = string.Empty;
}

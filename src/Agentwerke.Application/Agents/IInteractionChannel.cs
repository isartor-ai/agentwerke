using Agentwerke.Domain.Persistence;

namespace Agentwerke.Application.Agents;

/// <summary>
/// Everything a channel adapter needs to render an interaction. Deliberately narrow: prompt, choices,
/// identifiers and a link — never run context, artifacts, tool output or credentials, because this
/// payload leaves the trust boundary (#220).
/// </summary>
public sealed record InteractionDeliveryRequest(
    AgentInteraction Interaction,
    string RunId,
    string? WorkflowName,
    string RespondUrl);

/// <summary>The outcome of one delivery attempt to one channel.</summary>
public sealed record InteractionDeliveryResult(
    string Status,
    string? ChannelMessageId,
    string? Error)
{
    public static InteractionDeliveryResult Delivered(string? channelMessageId = null) =>
        new(InteractionDeliveryStatuses.Delivered, channelMessageId, null);

    public static InteractionDeliveryResult Failed(string error) =>
        new(InteractionDeliveryStatuses.Failed, null, error);

    /// <summary>
    /// The channel cannot carry a response for this interaction — Teams outbound-only in v1, say. The
    /// router records this rather than letting a blocking question land somewhere nobody can answer
    /// from, silently.
    /// </summary>
    public static InteractionDeliveryResult NotSupported(string reason) =>
        new(InteractionDeliveryStatuses.NotSupported, null, reason);
}

/// <summary>
/// A provider-neutral outbound channel for interactions (#220).
///
/// Implementations live in Agentwerke.Integrations and are bound by DI, mirroring how
/// ConnectorApprovalNotifier implements IApprovalNotifier. No provider type may be referenced from
/// Agentwerke.Domain, Agentwerke.Agents or Agentwerke.Application — an architecture test enforces it,
/// because this is the constraint most likely to erode under a deadline.
/// </summary>
public interface IInteractionChannel
{
    /// <summary>One of <see cref="InteractionChannels"/>.</summary>
    string ChannelId { get; }

    bool Enabled { get; }

    /// <summary>
    /// Whether a responder can answer from this channel. False for one-way channels such as a Teams
    /// incoming webhook, which is a fact about the provider rather than a missing feature.
    /// </summary>
    bool CanCarryResponse { get; }

    /// <summary>
    /// Delivers the interaction. Implementations should return <see cref="InteractionDeliveryResult.Failed"/>
    /// rather than throw, but the router treats a throw as a failure regardless: a channel outage must
    /// never fail the agent step that asked the question.
    /// </summary>
    Task<InteractionDeliveryResult> DeliverAsync(
        InteractionDeliveryRequest request,
        CancellationToken cancellationToken);
}

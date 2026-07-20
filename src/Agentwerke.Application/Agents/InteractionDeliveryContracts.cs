using Agentwerke.Domain.Persistence;

namespace Agentwerke.Application.Agents;

/// <summary>
/// Persistence for per-channel delivery attempts (#218). Kept separate from
/// <see cref="IAgentInteractionRepository"/> because the router (#220) tracks deliveries without
/// transitioning interactions, and the inbound channel adapters (#224, #225) correlate a provider's
/// message id back to an interaction without touching interaction state.
/// </summary>
public interface IInteractionDeliveryRepository
{
    /// <summary>
    /// Inserts or updates the delivery row for (interaction, channel). Keyed on the unique
    /// (InteractionId, Channel) index, which is the router's idempotency anchor: a retry of the same
    /// channel updates the attempt count rather than adding a second row.
    /// </summary>
    Task UpsertAsync(InteractionDelivery delivery, CancellationToken cancellationToken);

    /// <summary>All delivery rows for an interaction, oldest first.</summary>
    Task<IReadOnlyList<InteractionDelivery>> GetByInteractionAsync(
        string interactionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the delivery a provider's callback refers to, e.g. a Slack message ts. Returns null
    /// when the message is unknown, so an inbound callback for a stale message can be rejected.
    /// </summary>
    Task<InteractionDelivery?> GetByChannelMessageAsync(
        string channel,
        string channelMessageId,
        CancellationToken cancellationToken);
}

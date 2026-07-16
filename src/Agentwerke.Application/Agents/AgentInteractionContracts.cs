using Agentwerke.Domain.Persistence;

namespace Agentwerke.Application.Agents;

/// <summary>Outcome of an attempt to move an interaction to a terminal status (#218).</summary>
public enum InteractionTransitionOutcome
{
    /// <summary>This caller performed the transition. Only this caller may resume the run.</summary>
    Won,

    /// <summary>
    /// The interaction was already terminal — a duplicate response, a second channel that lost the
    /// race, a late reply, or the timeout sweeper arriving after an answer. Not an error.
    /// </summary>
    AlreadyTerminal,

    NotFound,
}

/// <summary>
/// The result of <see cref="IAgentInteractionRepository.TryTransitionAsync"/>. On
/// <see cref="InteractionTransitionOutcome.AlreadyTerminal"/> the interaction carries the winner's
/// state, so callers can report which channel actually answered.
/// </summary>
public sealed record InteractionTransitionResult(
    InteractionTransitionOutcome Outcome,
    AgentInteraction? Interaction);

/// <summary>
/// Persistence for the unified agent-interaction store (#192): the agent-to-agent coordination
/// bus today, and questions / delegations / human asks in later phases.
/// </summary>
public interface IAgentInteractionRepository
{
    Task AddAsync(AgentInteraction interaction, CancellationToken cancellationToken);

    /// <summary>All interactions for a run, oldest first — the run "conversation".</summary>
    Task<IReadOnlyList<AgentInteraction>> GetByRunAsync(string runId, CancellationToken cancellationToken);

    /// <summary>
    /// Coordination-bus messages for a run (<see cref="AgentInteractionKinds.Post"/>), oldest first,
    /// optionally filtered to a single sender.
    /// </summary>
    Task<IReadOnlyList<AgentInteraction>> GetPostsForRunAsync(
        string runId,
        string? fromFilter,
        CancellationToken cancellationToken);

    Task<AgentInteraction?> GetByIdAsync(string interactionId, CancellationToken cancellationToken);

    /// <summary>The oldest still-pending interaction for a run, if any.</summary>
    Task<AgentInteraction?> GetPendingForRunAsync(string runId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically moves a pending interaction to a terminal status (#218). Exactly one concurrent
    /// caller receives <see cref="InteractionTransitionOutcome.Won"/>; every other concurrent or late
    /// caller receives <see cref="InteractionTransitionOutcome.AlreadyTerminal"/> and must not act.
    ///
    /// This is the single-winner mechanism the whole interaction-channels epic (#215) rests on: once
    /// a question is fanned out to the UI and an external channel, two responders can answer at the
    /// same instant. Callers must enqueue the outbox Resume only on Won — anything else resumes the
    /// run more than once.
    /// </summary>
    Task<InteractionTransitionResult> TryTransitionAsync(
        string interactionId,
        string toStatus,
        string? response,
        string? respondedBy,
        string? respondedChannel,
        CancellationToken cancellationToken);

    /// <summary>Pending interactions, oldest first, optionally filtered by run and addressee type.</summary>
    Task<IReadOnlyList<AgentInteraction>> GetPendingAsync(
        string? runId,
        string? addresseeType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pending interactions whose <see cref="AgentInteraction.TimeoutAt"/> is set and at or before
    /// <paramref name="nowIso"/>. Rows with no timeout never expire. For the sweeper (#221).
    /// </summary>
    Task<IReadOnlyList<AgentInteraction>> GetDueForExpiryAsync(
        string nowIso,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

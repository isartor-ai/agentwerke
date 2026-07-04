using Agentwerke.Domain.Persistence;

namespace Agentwerke.Application.Agents;

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

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

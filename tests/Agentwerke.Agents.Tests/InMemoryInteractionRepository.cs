using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;

namespace Agentwerke.Agents.Tests;

/// <summary>Shared in-memory <see cref="IAgentInteractionRepository"/> for gateway/runner tests.</summary>
internal sealed class InMemoryInteractionRepository : IAgentInteractionRepository
{
    /// <summary>
    /// Stands in for the database's atomicity. <see cref="TryTransitionAsync"/> must read-and-write
    /// under one lock, or a concurrent test would let every caller observe Pending and all "win" —
    /// passing while the real single-winner guarantee (#218) went untested.
    /// </summary>
    private readonly Lock _gate = new();

    public List<AgentInteraction> Items { get; } = [];

    public Task AddAsync(AgentInteraction interaction, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Items.Add(interaction);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgentInteraction>> GetByRunAsync(string runId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentInteraction>>(
            Items.Where(i => i.RunId == runId).OrderBy(i => i.CreatedAt).ToList());

    public Task<IReadOnlyList<AgentInteraction>> GetPostsForRunAsync(
        string runId,
        string? fromFilter,
        CancellationToken cancellationToken)
    {
        var query = Items.Where(i => i.RunId == runId && i.Kind == AgentInteractionKinds.Post);
        if (!string.IsNullOrWhiteSpace(fromFilter))
        {
            query = query.Where(i => i.FromAgent == fromFilter);
        }

        return Task.FromResult<IReadOnlyList<AgentInteraction>>(query.OrderBy(i => i.CreatedAt).ToList());
    }

    public Task<AgentInteraction?> GetByIdAsync(string interactionId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(i => i.Id == interactionId));

    public Task<AgentInteraction?> GetPendingForRunAsync(string runId, CancellationToken cancellationToken) =>
        Task.FromResult(Items
            .Where(i => i.RunId == runId && i.Status == AgentInteractionStatuses.Pending)
            .OrderBy(i => i.CreatedAt)
            .FirstOrDefault());

    public Task<InteractionTransitionResult> TryTransitionAsync(
        string interactionId,
        string toStatus,
        string? response,
        string? respondedBy,
        string? respondedChannel,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var interaction = Items.FirstOrDefault(i => i.Id == interactionId);
            if (interaction is null)
            {
                return Task.FromResult(
                    new InteractionTransitionResult(InteractionTransitionOutcome.NotFound, null));
            }

            if (AgentInteractionStatuses.IsTerminal(interaction.Status))
            {
                return Task.FromResult(
                    new InteractionTransitionResult(InteractionTransitionOutcome.AlreadyTerminal, interaction));
            }

            if (response is not null)
            {
                interaction.Response = response;
            }

            if (respondedBy is not null)
            {
                interaction.RespondedBy = respondedBy;
                interaction.RespondedAt = DateTimeOffset.UtcNow.ToString("o");
            }

            if (respondedChannel is not null)
            {
                interaction.RespondedChannel = respondedChannel;
            }

            interaction.Status = toStatus;
            interaction.Version++;

            return Task.FromResult(
                new InteractionTransitionResult(InteractionTransitionOutcome.Won, interaction));
        }
    }

    public Task<IReadOnlyList<AgentInteraction>> GetPendingAsync(
        string? runId,
        string? addresseeType,
        CancellationToken cancellationToken)
    {
        var query = Items.Where(i => i.Status == AgentInteractionStatuses.Pending);

        if (!string.IsNullOrWhiteSpace(runId))
        {
            query = query.Where(i => i.RunId == runId);
        }

        if (!string.IsNullOrWhiteSpace(addresseeType))
        {
            query = query.Where(i => i.AddresseeType == addresseeType);
        }

        return Task.FromResult<IReadOnlyList<AgentInteraction>>(query.OrderBy(i => i.CreatedAt).ToList());
    }

    public Task<IReadOnlyList<AgentInteraction>> GetDueForExpiryAsync(
        string nowIso,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentInteraction>>(Items
            .Where(i => i.Status == AgentInteractionStatuses.Pending
                        && i.TimeoutAt is not null
                        && string.CompareOrdinal(i.TimeoutAt, nowIso) <= 0)
            .OrderBy(i => i.TimeoutAt)
            .ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

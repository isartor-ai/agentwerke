using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;

namespace Agentwerke.Agents.Tests;

/// <summary>Shared in-memory <see cref="IAgentInteractionRepository"/> for gateway/runner tests.</summary>
internal sealed class InMemoryInteractionRepository : IAgentInteractionRepository
{
    public List<AgentInteraction> Items { get; } = [];

    public Task AddAsync(AgentInteraction interaction, CancellationToken cancellationToken)
    {
        Items.Add(interaction);
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

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

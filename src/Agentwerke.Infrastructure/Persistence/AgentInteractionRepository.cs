using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agentwerke.Infrastructure.Persistence;

public sealed class AgentInteractionRepository : IAgentInteractionRepository
{
    private readonly AgentwerkeDbContext _dbContext;

    public AgentInteractionRepository(AgentwerkeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AgentInteraction interaction, CancellationToken cancellationToken)
    {
        await _dbContext.AgentInteractions.AddAsync(interaction, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentInteraction>> GetByRunAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AgentInteractions
            .Where(i => i.RunId == runId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentInteraction>> GetPostsForRunAsync(
        string runId,
        string? fromFilter,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.AgentInteractions
            .Where(i => i.RunId == runId && i.Kind == AgentInteractionKinds.Post);

        if (!string.IsNullOrWhiteSpace(fromFilter))
        {
            query = query.Where(i => i.FromAgent == fromFilter);
        }

        return await query
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<AgentInteraction?> GetByIdAsync(string interactionId, CancellationToken cancellationToken)
    {
        return _dbContext.AgentInteractions
            .FirstOrDefaultAsync(i => i.Id == interactionId, cancellationToken);
    }

    public Task<AgentInteraction?> GetPendingForRunAsync(string runId, CancellationToken cancellationToken)
    {
        return _dbContext.AgentInteractions
            .Where(i => i.RunId == runId && i.Status == AgentInteractionStatuses.Pending)
            .OrderBy(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}

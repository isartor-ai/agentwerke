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

    public async Task<InteractionTransitionResult> TryTransitionAsync(
        string interactionId,
        string toStatus,
        string? response,
        string? respondedBy,
        string? respondedChannel,
        CancellationToken cancellationToken)
    {
        var interaction = await _dbContext.AgentInteractions
            .FirstOrDefaultAsync(i => i.Id == interactionId, cancellationToken);

        if (interaction is null)
        {
            return new InteractionTransitionResult(InteractionTransitionOutcome.NotFound, null);
        }

        // Fast path for a response that arrives long after the winner: no write, no exception.
        if (AgentInteractionStatuses.IsTerminal(interaction.Status))
        {
            return new InteractionTransitionResult(InteractionTransitionOutcome.AlreadyTerminal, interaction);
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

        // Bump the token explicitly. EF compares the *original* loaded value in the UPDATE's WHERE
        // clause, so incrementing here makes a concurrent writer's UPDATE match zero rows and throw.
        // Without this, a transition that happened to change nothing else would not be guarded.
        interaction.Version++;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone transitioned this row between our read and our write. Reload so the caller
            // reports the winner's status, responder, and channel rather than our discarded attempt.
            await _dbContext.Entry(interaction).ReloadAsync(cancellationToken);
            return new InteractionTransitionResult(InteractionTransitionOutcome.AlreadyTerminal, interaction);
        }

        return new InteractionTransitionResult(InteractionTransitionOutcome.Won, interaction);
    }

    public async Task<IReadOnlyList<AgentInteraction>> GetPendingAsync(
        string? runId,
        string? addresseeType,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.AgentInteractions
            .Where(i => i.Status == AgentInteractionStatuses.Pending);

        if (!string.IsNullOrWhiteSpace(runId))
        {
            query = query.Where(i => i.RunId == runId);
        }

        if (!string.IsNullOrWhiteSpace(addresseeType))
        {
            query = query.Where(i => i.AddresseeType == addresseeType);
        }

        return await query.OrderBy(i => i.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentInteraction>> GetDueForExpiryAsync(
        string nowIso,
        CancellationToken cancellationToken)
    {
        // TimeoutAt is written as a UTC ISO-8601 "o" string, so every value has the same length and
        // offset and ordinal ordering matches chronological ordering. A local-time or non-"o" value
        // would compare wrong here and silently never expire — see #217's schema notes.
        return await _dbContext.AgentInteractions
            .Where(i => i.Status == AgentInteractionStatuses.Pending
                        && i.TimeoutAt != null
                        && string.Compare(i.TimeoutAt, nowIso) <= 0)
            .OrderBy(i => i.TimeoutAt)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}

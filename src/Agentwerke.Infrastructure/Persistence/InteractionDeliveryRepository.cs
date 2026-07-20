using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agentwerke.Infrastructure.Persistence;

public sealed class InteractionDeliveryRepository : IInteractionDeliveryRepository
{
    private readonly AgentwerkeDbContext _dbContext;

    public InteractionDeliveryRepository(AgentwerkeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task UpsertAsync(InteractionDelivery delivery, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.InteractionDeliveries
            .FirstOrDefaultAsync(
                d => d.InteractionId == delivery.InteractionId && d.Channel == delivery.Channel,
                cancellationToken);

        if (existing is null)
        {
            if (string.IsNullOrWhiteSpace(delivery.Id))
            {
                delivery.Id = Guid.NewGuid().ToString("n");
            }

            if (string.IsNullOrWhiteSpace(delivery.CreatedAt))
            {
                delivery.CreatedAt = DateTimeOffset.UtcNow.ToString("o");
            }

            await _dbContext.InteractionDeliveries.AddAsync(delivery, cancellationToken);
        }
        else
        {
            existing.Status = delivery.Status;
            existing.Attempts = delivery.Attempts;
            existing.LastAttemptAt = delivery.LastAttemptAt;
            existing.LastError = delivery.LastError;

            // A retry that fails carries no message id; keep the one from the successful send so the
            // channel adapter can still update the original message.
            if (delivery.ChannelMessageId is not null)
            {
                existing.ChannelMessageId = delivery.ChannelMessageId;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InteractionDelivery>> GetByInteractionAsync(
        string interactionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.InteractionDeliveries
            .Where(d => d.InteractionId == interactionId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<InteractionDelivery?> GetByChannelMessageAsync(
        string channel,
        string channelMessageId,
        CancellationToken cancellationToken)
    {
        return _dbContext.InteractionDeliveries
            .FirstOrDefaultAsync(
                d => d.Channel == channel && d.ChannelMessageId == channelMessageId,
                cancellationToken);
    }
}

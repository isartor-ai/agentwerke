using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agentwerke.Infrastructure.Persistence;

public sealed class ExternalWorkflowEventRepository : IExternalWorkflowEventRepository
{
    private readonly AgentwerkeDbContext _dbContext;

    public ExternalWorkflowEventRepository(AgentwerkeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ExternalWorkflowEvent @event, CancellationToken cancellationToken)
    {
        _dbContext.ExternalWorkflowEvents.Add(@event);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryAddAsync(ExternalWorkflowEvent @event, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(@event.DeliveryId))
        {
            await AddAsync(@event, cancellationToken);
            return true;
        }

        if (await DeliveryExistsAsync(@event.DeliveryId, cancellationToken))
        {
            return false;
        }

        _dbContext.ExternalWorkflowEvents.Add(@event);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Two deliveries of the same event can pass the check above concurrently; the unique
            // index breaks the tie. Detach before re-querying so the failed insert isn't retried
            // by the next SaveChanges on this context.
            _dbContext.Entry(@event).State = EntityState.Detached;
            if (await DeliveryExistsAsync(@event.DeliveryId, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    private Task<bool> DeliveryExistsAsync(string deliveryId, CancellationToken cancellationToken) =>
        _dbContext.ExternalWorkflowEvents
            .AsNoTracking()
            .AnyAsync(e => e.DeliveryId == deliveryId, cancellationToken);
}

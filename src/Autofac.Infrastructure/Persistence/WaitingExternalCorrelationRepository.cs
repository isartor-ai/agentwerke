using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Infrastructure.Persistence;

public sealed class WaitingExternalCorrelationRepository : IWaitingExternalCorrelationRepository
{
    private readonly AutofacDbContext _dbContext;

    public WaitingExternalCorrelationRepository(AutofacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task UpsertAsync(WaitingExternalCorrelation correlation, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.WaitingExternalCorrelations
            .FirstOrDefaultAsync(c => c.RunId == correlation.RunId, cancellationToken);

        if (existing is not null)
        {
            existing.CorrelationKey = correlation.CorrelationKey;
            existing.MessageName = correlation.MessageName;
            existing.CreatedAt = correlation.CreatedAt;
        }
        else
        {
            _dbContext.WaitingExternalCorrelations.Add(correlation);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string runId, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.WaitingExternalCorrelations
            .FirstOrDefaultAsync(c => c.RunId == runId, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.WaitingExternalCorrelations.Remove(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> FindWaitingRunIdAsync(string messageName, string correlationKey, CancellationToken cancellationToken)
    {
        var match = await _dbContext.WaitingExternalCorrelations
            .AsNoTracking()
            .Where(c => c.MessageName == messageName && c.CorrelationKey == correlationKey)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return match?.RunId;
    }
}

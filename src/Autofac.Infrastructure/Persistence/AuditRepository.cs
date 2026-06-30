using Autofac.Application.Observability;
using Autofac.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Infrastructure.Persistence;

public sealed class AuditRepository : IAuditRepository, IAuditQuery
{
    private readonly AutofacDbContext _dbContext;

    public AuditRepository(AutofacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        _dbContext.AuditRecords.Add(record);
        await Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditRecord>> QueryAsync(string? runId, int limit, CancellationToken cancellationToken)
    {
        var query = _dbContext.AuditRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(runId))
        {
            query = query.Where(record => record.RunId == runId);
        }

        // Timestamps are ISO-8601 ("o"), so lexical ordering is chronological.
        return await query
            .OrderByDescending(record => record.Timestamp)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
    }
}

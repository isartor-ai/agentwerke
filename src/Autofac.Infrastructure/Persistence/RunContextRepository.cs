using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Infrastructure.Persistence;

public sealed class RunContextRepository : IRunContextRepository
{
    private readonly AutofacDbContext _dbContext;

    public RunContextRepository(AutofacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SetAsync(string runId, string key, string value, string kind, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        var existing = await _dbContext.RunContextEntries
            .FirstOrDefaultAsync(e => e.RunId == runId && e.Key == key, cancellationToken);

        if (existing is not null)
        {
            existing.Value = value;
            existing.Kind = kind;
            existing.UpdatedAt = now;
        }
        else
        {
            _dbContext.RunContextEntries.Add(new RunContextEntry
            {
                Id = $"ctx_{Guid.NewGuid():N}",
                RunId = runId,
                Key = key,
                Value = value,
                Kind = kind,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RunContextEntry>> GetAllAsync(string runId, CancellationToken cancellationToken)
    {
        return await _dbContext.RunContextEntries
            .AsNoTracking()
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}

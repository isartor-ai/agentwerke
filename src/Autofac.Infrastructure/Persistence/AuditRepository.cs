using Autofac.Application.Observability;
using Autofac.Domain.Persistence;

namespace Autofac.Infrastructure.Persistence;

public sealed class AuditRepository : IAuditRepository
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
}

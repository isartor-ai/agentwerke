using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;

namespace Autofac.Infrastructure.Persistence;

public sealed class ExternalWorkflowEventRepository : IExternalWorkflowEventRepository
{
    private readonly AutofacDbContext _dbContext;

    public ExternalWorkflowEventRepository(AutofacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ExternalWorkflowEvent @event, CancellationToken cancellationToken)
    {
        _dbContext.ExternalWorkflowEvents.Add(@event);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

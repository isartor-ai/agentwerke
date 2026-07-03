using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;

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
}

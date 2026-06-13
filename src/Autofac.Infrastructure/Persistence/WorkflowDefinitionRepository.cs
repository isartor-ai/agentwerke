using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Infrastructure.Persistence;

public sealed class WorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private readonly AutofacDbContext _dbContext;

    public WorkflowDefinitionRepository(AutofacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .OrderBy(workflow => workflow.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkflowDefinition?> GetAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(workflow => workflow.Id == workflowId, cancellationToken);
    }

    public async Task<WorkflowDefinition?> FindTrackedAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.WorkflowDefinitions
            .FirstOrDefaultAsync(workflow => workflow.Id == workflowId, cancellationToken);
    }

    public Task AddAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default)
    {
        _dbContext.WorkflowDefinitions.Add(workflow);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}

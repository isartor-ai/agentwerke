using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Infrastructure.Persistence;

public sealed class WorkflowRunRepository : IWorkflowRunRepository
{
    private readonly AutofacDbContext _dbContext;

    public WorkflowRunRepository(AutofacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        return _dbContext.WorkflowRuns
            .AsNoTracking()
            .Include(r => r.Steps)
            .Include(r => r.Events)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
    }
}

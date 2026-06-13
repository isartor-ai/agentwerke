using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Infrastructure.Persistence;

public sealed class ApprovalRepository : IApprovalRepository
{
    private readonly AutofacDbContext _dbContext;

    public ApprovalRepository(AutofacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ApprovalRequest?> GetApprovalAsync(string approvalId, CancellationToken cancellationToken)
    {
        return _dbContext.ApprovalRequests
            .FirstOrDefaultAsync(a => a.Id == approvalId, cancellationToken);
    }

    public Task<ApprovalRequest?> GetPendingApprovalForRunAsync(string runId, CancellationToken cancellationToken)
    {
        return _dbContext.ApprovalRequests
            .Where(a => a.RunId == runId && a.Status == "pending")
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddApprovalAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        await _dbContext.ApprovalRequests.AddAsync(approval, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}

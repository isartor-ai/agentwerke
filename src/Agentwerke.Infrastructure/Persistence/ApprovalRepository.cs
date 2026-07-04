using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agentwerke.Infrastructure.Persistence;

public sealed class ApprovalRepository : IApprovalRepository
{
    private readonly AgentwerkeDbContext _dbContext;

    public ApprovalRepository(AgentwerkeDbContext dbContext)
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

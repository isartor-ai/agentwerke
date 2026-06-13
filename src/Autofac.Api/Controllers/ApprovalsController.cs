using Autofac.Api.Contracts.Approvals;
using Autofac.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/approvals")]
public sealed class ApprovalsController : ControllerBase
{
    private readonly AutofacDbContext _dbContext;

    public ApprovalsController(AutofacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var approvals = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Select(a => new ApprovalSummary(
                a.Id,
                a.RunId,
                a.WorkflowName,
                a.ActionRequested,
                a.Requester,
                a.AgentName,
                a.PolicyRationale,
                a.RiskScore,
                a.RiskLevel,
                a.RiskFactors,
                a.AffectedSystems,
                DateTimeOffset.Parse(a.SlaDeadline),
                DateTimeOffset.Parse(a.CreatedAt),
                a.Status,
                a.Priority))
            .ToListAsync();

        return Ok(approvals);
    }

    [HttpPost("{approvalId}/decision")]
    public async Task<IActionResult> Decide(string approvalId, [FromBody] ApprovalDecisionRequest request)
    {
        var approval = await _dbContext.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == approvalId);
        if (approval == null)
        {
            return NotFound();
        }

        approval.Status = request.Decision;
        approval.DecisionComment = request.Comment;
        approval.DecidedAt = DateTime.UtcNow.ToString("o");
        approval.DecidedBy = "api-user"; // In a real app, this would be the authenticated user

        await _dbContext.SaveChangesAsync();

        return Accepted(new ApprovalDecisionResponse(
            ApprovalId: approvalId,
            Status: approval.Status,
            DecidedAt: DateTimeOffset.Parse(approval.DecidedAt),
            DecidedBy: approval.DecidedBy,
            Comment: approval.DecisionComment));
    }
}
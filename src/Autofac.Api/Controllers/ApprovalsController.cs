using Autofac.Api.Contracts;
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
            .ToListAsync();

        return Ok(approvals.Select(ApiContractMappings.ToApprovalSummary).ToList());
    }

    [HttpGet("{approvalId}")]
    public async Task<IActionResult> Get(string approvalId)
    {
        var approval = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == approvalId);

        if (approval == null)
        {
            return NotFound();
        }

        return Ok(ApiContractMappings.ToApprovalSummary(approval));
    }

    [HttpPost("{approvalId}/decision")]
    public async Task<IActionResult> Decide(string approvalId, [FromBody] ApprovalDecisionRequest request)
    {
        var approval = await _dbContext.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == approvalId);
        if (approval == null)
        {
            return NotFound();
        }

        var resolvedStatus = request.Decision switch
        {
            "approve" => "approved",
            "reject" => "rejected",
            "escalate" => "escalated",
            _ => null
        };

        if (resolvedStatus is null)
        {
            return BadRequest(new { message = $"Unsupported approval decision '{request.Decision}'." });
        }

        approval.Status = resolvedStatus;
        approval.DecisionComment = request.Comment;
        approval.DecidedAt = DateTime.UtcNow.ToString("o");
        approval.DecidedBy = "api-user"; // In a real app, this would be the authenticated user

        await _dbContext.SaveChangesAsync();

        return Accepted(new ApprovalDecisionResponse(
            ApprovalId: approvalId,
            Status: approval.Status,
            DecidedAt: approval.DecidedAt,
            DecidedBy: approval.DecidedBy ?? string.Empty,
            Comment: approval.DecisionComment));
    }
}

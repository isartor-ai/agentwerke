using Agentwerke.Api.Auth;
using Agentwerke.Api.Contracts;
using Agentwerke.Api.Contracts.Approvals;
using Agentwerke.Application.Workflows;
using Agentwerke.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agentwerke.Api.Controllers;

[ApiController]
[Route("api/approvals")]
[Authorize(Policy = AgentwerkePolicies.Viewer)]
public sealed class ApprovalsController : ControllerBase
{
    private readonly AgentwerkeDbContext _dbContext;
    private readonly IWorkflowRunOrchestrationService _orchestrationService;

    public ApprovalsController(
        AgentwerkeDbContext dbContext,
        IWorkflowRunOrchestrationService orchestrationService)
    {
        _dbContext = dbContext;
        _orchestrationService = orchestrationService;
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

    [Authorize(Policy = AgentwerkePolicies.Approver)]
    [HttpPost("{approvalId}/decision")]
    public async Task<IActionResult> Decide(string approvalId, [FromBody] ApprovalDecisionRequest request)
    {
        var validDecisions = new[] { "approve", "reject", "escalate" };
        if (!validDecisions.Contains(request.Decision, StringComparer.Ordinal))
        {
            return BadRequest(new { message = $"Unsupported approval decision '{request.Decision}'." });
        }

        try
        {
            var approval = await _dbContext.ApprovalRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == approvalId);

            if (approval == null)
            {
                return NotFound();
            }

            var decidedBy = AuthenticatedPrincipal.ResolveSubject(User);

            await _orchestrationService.ResumeRunAsync(
                new ResumeRunCommand(
                    RunId: approval.RunId,
                    ApprovalId: approvalId,
                    Decision: request.Decision,
                    Comment: request.Comment,
                    DecidedBy: decidedBy),
                HttpContext.RequestAborted);

            var updated = await _dbContext.ApprovalRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == approvalId);

            return Accepted(ApiContractMappings.ToApprovalDecisionResponse(approvalId, updated!));
        }
        catch (ApprovalNotFoundException)
        {
            return NotFound();
        }
        catch (ApprovalNotPendingException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (WorkflowRunNotFoundException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
    }
}

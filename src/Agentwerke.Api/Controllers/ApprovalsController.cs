using Agentwerke.Api.Auth;
using Agentwerke.Api.Contracts;
using Agentwerke.Api.Contracts.Approvals;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
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

    /// <summary>
    /// Pending tool-access escalations (#202) across all runs: an agent asked for a tool its
    /// step does not allow. Decisions go through the run's interaction-answer endpoint.
    /// </summary>
    [HttpGet("tool-access")]
    public async Task<IActionResult> ListToolAccessRequests(CancellationToken cancellationToken)
    {
        var requests = await (
            from interaction in _dbContext.AgentInteractions.AsNoTracking()
            where interaction.Kind == AgentInteractionKinds.ToolAccess &&
                  interaction.Status == AgentInteractionStatuses.Pending
            join run in _dbContext.WorkflowRuns.AsNoTracking()
                on interaction.RunId equals run.Id into runs
            from run in runs.DefaultIfEmpty()
            join step in _dbContext.WorkflowRunSteps.AsNoTracking()
                on interaction.StepId equals step.Id into steps
            from step in steps.DefaultIfEmpty()
            orderby interaction.CreatedAt
            select new ToolAccessRequestSummary(
                interaction.Id,
                interaction.RunId,
                run != null ? run.WorkflowName : null,
                interaction.StepId,
                step != null ? step.Name : null,
                interaction.FromAgent,
                interaction.ToolName,
                interaction.Intent,
                interaction.Prompt,
                interaction.Options,
                interaction.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(requests);
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

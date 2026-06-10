using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/approvals")]
public sealed class ApprovalsController : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        var approvals = new[]
        {
            new ApprovalSummary(
                Id: "apr-1001",
                RunId: "run-0421",
                WorkflowName: "GitHub PR Review",
                ActionRequested: "Merge branch feature/auth-refactor to main",
                Requester: "alice@example.com",
                AgentName: "GitAgent",
                PolicyRationale: "Policy requires review.",
                RiskScore: 72,
                RiskLevel: "high",
                RiskFactors: new[] { "production" },
                AffectedSystems: new[] { "production/api" },
                SlaDeadline: DateTimeOffset.UtcNow.AddHours(2),
                CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-20),
                Status: "pending",
                Priority: "high")
        };

        return Ok(approvals);
    }

    [HttpPost("{approvalId}/decision")]
    public IActionResult Decide(string approvalId, [FromBody] ApprovalDecisionRequest request)
    {
        return Accepted(new ApprovalDecisionResponse(
            ApprovalId: approvalId,
            Status: request.Decision,
            DecidedAt: DateTimeOffset.UtcNow,
            DecidedBy: "api-user",
            Comment: request.Comment));
    }

    public sealed record ApprovalSummary(
        string Id,
        string RunId,
        string WorkflowName,
        string ActionRequested,
        string Requester,
        string AgentName,
        string PolicyRationale,
        int RiskScore,
        string RiskLevel,
        IReadOnlyList<string> RiskFactors,
        IReadOnlyList<string> AffectedSystems,
        DateTimeOffset SlaDeadline,
        DateTimeOffset CreatedAt,
        string Status,
        string Priority);

    public sealed record ApprovalDecisionRequest(string Decision, string? Comment);

    public sealed record ApprovalDecisionResponse(
        string ApprovalId,
        string Status,
        DateTimeOffset DecidedAt,
        string DecidedBy,
        string? Comment);
}
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/workflows")]
public sealed class WorkflowsController : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        var workflows = new[]
        {
            new WorkflowSummary("wf_bootstrap", "Bootstrap Platform", "draft"),
            new WorkflowSummary("wf_review", "Policy Review", "draft")
        };

        return Ok(workflows);
    }

    [HttpGet("{workflowId}")]
    public IActionResult Get(string workflowId)
    {
        return Ok(new WorkflowDetail(
            workflowId,
            $"Workflow {workflowId}",
            "draft",
            DateTimeOffset.UtcNow));
    }

    public sealed record WorkflowSummary(string WorkflowId, string Name, string Status);

    public sealed record WorkflowDetail(
        string WorkflowId,
        string Name,
        string Status,
        DateTimeOffset UpdatedAtUtc);
}

using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/runs")]
public sealed class RunsController : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        var runs = new[]
        {
            new RunSummary("run_001", "wf_bootstrap", "running"),
            new RunSummary("run_002", "wf_review", "completed")
        };

        return Ok(runs);
    }

    [HttpGet("{runId}")]
    public IActionResult Get(string runId)
    {
        return Ok(new RunDetail(
            runId,
            "wf_bootstrap",
            "running",
            DateTimeOffset.UtcNow.AddMinutes(-3)));
    }

    [HttpPost]
    public IActionResult Start([FromBody] StartRunRequest request)
    {
        var runId = $"run_{Guid.NewGuid():N}";

        return Accepted(new StartRunResponse(
            runId,
            request.WorkflowId,
            "accepted"));
    }

    public sealed record RunSummary(string RunId, string WorkflowId, string Status);

    public sealed record RunDetail(
        string RunId,
        string WorkflowId,
        string Status,
        DateTimeOffset StartedAtUtc);

    public sealed record StartRunRequest(string WorkflowId);

    public sealed record StartRunResponse(string RunId, string WorkflowId, string Status);
}

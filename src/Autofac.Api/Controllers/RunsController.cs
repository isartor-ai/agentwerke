using Autofac.Storage.Artifacts;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/runs")]
public sealed class RunsController : ControllerBase
{
    private readonly IArtifactStorage _artifactStorage;

    public RunsController(IArtifactStorage artifactStorage)
    {
        _artifactStorage = artifactStorage;
    }

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

    [HttpPost("{runId}/artifacts/{artifactName}")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> UploadArtifact(
        string runId,
        string artifactName,
        CancellationToken cancellationToken)
    {
        await _artifactStorage.SaveAsync(
            runId,
            artifactName,
            Request.Body,
            cancellationToken);

        return Accepted(new
        {
            runId,
            artifactName,
            status = "stored"
        });
    }

    [HttpGet("{runId}/artifacts/{artifactName}")]
    public async Task<IActionResult> DownloadArtifact(
        string runId,
        string artifactName,
        CancellationToken cancellationToken)
    {
        if (!await _artifactStorage.ExistsAsync(runId, artifactName, cancellationToken))
        {
            return NotFound(new
            {
                runId,
                artifactName,
                error = "Artifact not found"
            });
        }

        var stream = await _artifactStorage.OpenReadAsync(runId, artifactName, cancellationToken);
        return File(stream, "application/octet-stream", artifactName);
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

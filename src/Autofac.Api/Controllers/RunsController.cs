using Autofac.Api.Contracts;
using Autofac.Api.Contracts.Runs;
using Autofac.Application.Workflows;
using Autofac.Infrastructure.Persistence;
using Autofac.Storage.Artifacts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/runs")]
public sealed class RunsController : ControllerBase
{
    private readonly AutofacDbContext _dbContext;
    private readonly IArtifactStorage _artifactStorage;
    private readonly IWorkflowRunOrchestrationService _orchestrationService;

    public RunsController(
        AutofacDbContext dbContext,
        IArtifactStorage artifactStorage,
        IWorkflowRunOrchestrationService orchestrationService)
    {
        _dbContext = dbContext;
        _artifactStorage = artifactStorage;
        _orchestrationService = orchestrationService;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var runs = await _dbContext.WorkflowRuns
            .AsNoTracking()
            .Include(r => r.Events)
            .ToListAsync();

        return Ok(runs.Select(ApiContractMappings.ToRunSummary).ToList());
    }

    [HttpGet("{runId}")]
    public async Task<IActionResult> Get(string runId)
    {
        var run = await _dbContext.WorkflowRuns
            .AsNoTracking()
            .Include(r => r.Steps)
            .Include(r => r.Events)
            .FirstOrDefaultAsync(r => r.Id == runId);

        if (run == null)
        {
            return NotFound();
        }

        return Ok(ApiContractMappings.ToRunDetail(run));
    }

    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartRunRequest request)
    {
        try
        {
            var result = await _orchestrationService.StartRunAsync(
                new StartRunCommand(request.WorkflowId, Initiator: "api"),
                HttpContext.RequestAborted);

            return Accepted(new StartRunResponse(
                result.RunId,
                result.WorkflowId,
                ApiContractMappings.NormalizeRunStatus(result.Status)));
        }
        catch (WorkflowNotFoundException)
        {
            return NotFound(new { message = $"Workflow with id '{request.WorkflowId}' not found." });
        }
        catch (WorkflowNotPublishedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{runId}/recover")]
    public async Task<IActionResult> Recover(string runId)
    {
        try
        {
            var result = await _orchestrationService.RecoverRunAsync(runId, HttpContext.RequestAborted);
            return Accepted(new { runId = result.RunId, status = ApiContractMappings.NormalizeRunStatus(result.Status) });
        }
        catch (WorkflowRunNotFoundException)
        {
            return NotFound(new { message = $"Run '{runId}' not found." });
        }
        catch (WorkflowNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
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
}

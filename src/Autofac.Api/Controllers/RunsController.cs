using Autofac.Api.Contracts;
using Autofac.Api.Contracts.Runs;
using Autofac.Domain.Persistence;
using Autofac.Infrastructure.Persistence;
using Autofac.Storage.Artifacts;
using Autofac.Workflows.Runtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/runs")]
public sealed class RunsController : ControllerBase
{
    private readonly AutofacDbContext _dbContext;
    private readonly IArtifactStorage _artifactStorage;
    private readonly IWorkflowInstanceEngine _workflowEngine;

    public RunsController(AutofacDbContext dbContext, IArtifactStorage artifactStorage, IWorkflowInstanceEngine workflowEngine)
    {
        _dbContext = dbContext;
        _artifactStorage = artifactStorage;
        _workflowEngine = workflowEngine;
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
        var workflow = await _dbContext.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(w => w.Id == request.WorkflowId);
        if (workflow == null)
        {
            return NotFound(new { message = $"Workflow with id '{request.WorkflowId}' not found." });
        }

        // This is a simplified start, in a real scenario we would have a more complex setup
        // to parse the bpmn and pass it to the engine.
        var run = await _workflowEngine.StartAsync(request.WorkflowId, new Workflows.Bpmn.BpmnWorkflowDefinition(workflow.Name, workflow.Name, new List<Workflows.Bpmn.BpmnNodeDefinition>()), "api", CancellationToken.None);

        return Accepted(new StartRunResponse(
            run.RunId,
            request.WorkflowId,
            ApiContractMappings.NormalizeRunStatus(run.Status)));
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

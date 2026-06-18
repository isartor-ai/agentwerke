using Autofac.Api.Auth;
using Autofac.Api.Contracts;
using Autofac.Api.Contracts.Runs;
using Autofac.Application.Workflows;
using Autofac.Infrastructure.Persistence;
using Autofac.Storage.Artifacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/runs")]
[Authorize(Policy = AutofacPolicies.Viewer)]
public sealed class RunsController : ControllerBase
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AutofacDbContext _dbContext;
    private readonly IArtifactStorage _artifactStorage;
    private readonly IWorkflowRunOrchestrationService _orchestrationService;
    private readonly IEvidencePackService _evidencePackService;

    public RunsController(
        AutofacDbContext dbContext,
        IArtifactStorage artifactStorage,
        IWorkflowRunOrchestrationService orchestrationService,
        IEvidencePackService evidencePackService)
    {
        _dbContext = dbContext;
        _artifactStorage = artifactStorage;
        _orchestrationService = orchestrationService;
        _evidencePackService = evidencePackService;
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

        var approvals = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Where(a => a.RunId == runId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var artifacts = await _artifactStorage.ListAsync(runId, HttpContext.RequestAborted);

        return Ok(ApiContractMappings.ToRunDetail(run, approvals, artifacts));
    }

    [Authorize(Policy = AutofacPolicies.Operator)]
    [HttpGet("{runId}/evidence-pack")]
    public async Task<IActionResult> GetEvidencePack(string runId)
    {
        try
        {
            var pack = await _evidencePackService.GenerateAsync(runId, HttpContext.RequestAborted);
            return Ok(pack);
        }
        catch (EvidencePackNotFoundException)
        {
            return NotFound(new { message = $"Run '{runId}' not found." });
        }
    }

    [Authorize(Policy = AutofacPolicies.Operator)]
    [HttpGet("{runId}/evidence-pack/download")]
    public async Task<IActionResult> DownloadEvidencePack(string runId)
    {
        try
        {
            var pack = await _evidencePackService.GenerateAsync(runId, HttpContext.RequestAborted);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(pack, EvidenceJsonOptions);
            var fileName = $"{runId}-evidence-pack.json";
            return File(bytes, "application/json", fileName);
        }
        catch (EvidencePackNotFoundException)
        {
            return NotFound(new { message = $"Run '{runId}' not found." });
        }
    }

    [Authorize(Policy = AutofacPolicies.Operator)]
    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartRunRequest request)
    {
        try
        {
            var result = await _orchestrationService.StartRunAsync(
                new StartRunCommand(request.WorkflowId, Initiator: AuthenticatedPrincipal.ResolveSubject(User)),
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

    [Authorize(Policy = AutofacPolicies.Operator)]
    [HttpPost("{runId}/cancel")]
    public async Task<IActionResult> Cancel(string runId)
    {
        var run = await _dbContext.WorkflowRuns
            .FirstOrDefaultAsync(r => r.Id == runId);

        if (run == null)
        {
            return NotFound(new { message = $"Run '{runId}' not found." });
        }

        var terminalStatuses = new[] { "completed", "failed", "cancelled" };
        if (Array.Exists(terminalStatuses, s => string.Equals(s, run.Status, StringComparison.Ordinal)))
        {
            return Conflict(new { message = $"Run '{runId}' is already in a terminal state '{run.Status}' and cannot be cancelled." });
        }

        run.Status = "cancelled";
        run.CompletedAt = DateTimeOffset.UtcNow.ToString("o");
        run.PendingApprovals = 0;
        await _dbContext.SaveChangesAsync();

        return Ok(new { runId, status = "cancelled" });
    }

    [Authorize(Policy = AutofacPolicies.Operator)]
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

    [Authorize(Policy = AutofacPolicies.Operator)]
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

    [HttpGet("{runId}/events/stream")]
    public async Task StreamEvents(string runId, CancellationToken cancellationToken)
    {
        var run = await _dbContext.WorkflowRuns
            .AsNoTracking()
            .Select(r => new { r.Id, r.Status })
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run == null)
        {
            Response.StatusCode = 404;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var terminalStatuses = new[] { "completed", "failed", "cancelled" };
        var seenIds = new HashSet<string>();

        var existing = await _dbContext.WorkflowEvents
            .AsNoTracking()
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var evt in existing)
        {
            seenIds.Add(evt.Id);
            await WriteSseEventAsync(evt, cancellationToken);
        }

        if (Array.Exists(terminalStatuses, s => string.Equals(s, run.Status, StringComparison.Ordinal)))
        {
            await Response.WriteAsync("event: done\ndata: {}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(2_000, cancellationToken);

            var seenList = seenIds.ToList();
            var fresh = await _dbContext.WorkflowEvents
                .AsNoTracking()
                .Where(e => e.RunId == runId && !seenList.Contains(e.Id))
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(cancellationToken);

            foreach (var evt in fresh)
            {
                seenIds.Add(evt.Id);
                await WriteSseEventAsync(evt, cancellationToken);
            }

            var current = await _dbContext.WorkflowRuns
                .AsNoTracking()
                .Select(r => new { r.Id, r.Status })
                .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

            if (current == null || Array.Exists(terminalStatuses, s => string.Equals(s, current.Status, StringComparison.Ordinal)))
            {
                await Response.WriteAsync("event: done\ndata: {}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                return;
            }
        }
    }

    private async Task WriteSseEventAsync(Domain.Persistence.WorkflowEvent evt, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(
            new { evt.Id, evt.RunId, evt.Type, evt.Message, evt.CreatedAt },
            SseJsonOptions);
        await Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
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

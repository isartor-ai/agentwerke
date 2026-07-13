using Agentwerke.Api.Auth;
using Agentwerke.Api.Contracts;
using Agentwerke.Api.Contracts.Runs;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Agentwerke.Infrastructure.Persistence;
using Agentwerke.Storage.Artifacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Agentwerke.Api.Controllers;

[ApiController]
[Route("api/runs")]
[Authorize(Policy = AgentwerkePolicies.Viewer)]
public sealed class RunsController : ControllerBase
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AgentwerkeDbContext _dbContext;
    private readonly IArtifactStorage _artifactStorage;
    private readonly IWorkflowRunOrchestrationService _orchestrationService;
    private readonly IEvidencePackService _evidencePackService;

    public RunsController(
        AgentwerkeDbContext dbContext,
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
            // Newest first. StartedAt is an ISO-8601 ("o") string, which sorts chronologically
            // lexicographically — otherwise the list comes back in arbitrary row order and the
            // most recent run is buried instead of at the top.
            .OrderByDescending(r => r.StartedAt)
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

    [Authorize(Policy = AgentwerkePolicies.Operator)]
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

    [Authorize(Policy = AgentwerkePolicies.Operator)]
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

    [Authorize(Policy = AgentwerkePolicies.Operator)]
    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartRunRequest request)
    {
        try
        {
            var result = await _orchestrationService.StartRunAsync(
                new StartRunCommand(
                    request.WorkflowId,
                    Initiator: AuthenticatedPrincipal.ResolveSubject(User),
                    Inputs: request.Inputs),
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

    [Authorize(Policy = AgentwerkePolicies.Operator)]
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

    [Authorize(Policy = AgentwerkePolicies.Operator)]
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

    [Authorize(Policy = AgentwerkePolicies.Operator)]
    [HttpPost("{runId}/resume-external")]
    public async Task<IActionResult> ResumeExternal(string runId, [FromBody] ResumeExternalRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CorrelationKey))
        {
            return BadRequest(new { message = "Correlation key is required." });
        }

        try
        {
            var result = await _orchestrationService.ResumeExternalRunAsync(
                new ResumeExternalRunCommand(
                    RunId: runId,
                    CorrelationKey: request.CorrelationKey,
                    Payload: request.Payload ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    ResumedBy: request.ResumedBy ?? AuthenticatedPrincipal.ResolveSubject(User)),
                HttpContext.RequestAborted);

            return Accepted(new { runId = result.RunId, status = ApiContractMappings.NormalizeRunStatus(result.Status) });
        }
        catch (WorkflowRunNotFoundException)
        {
            return NotFound(new { message = $"Run '{runId}' not found." });
        }
    }

    [Authorize(Policy = AgentwerkePolicies.Operator)]
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
        HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

        var terminalStatuses = new[] { "completed", "failed", "cancelled" };
        var streamState = new RunEventStreamState(runId);

        var existing = await streamState
            .BuildFreshEventsQuery(_dbContext.WorkflowEvents.AsNoTracking())
            .ToListAsync(cancellationToken);

        foreach (var evt in existing)
        {
            await WriteSseEventAsync(evt, cancellationToken);
        }
        streamState.MarkDelivered(existing);

        if (Array.Exists(terminalStatuses, s => string.Equals(s, run.Status, StringComparison.Ordinal)))
        {
            await Response.WriteAsync("event: done\ndata: {}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return;
        }

        var pollCount = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(RunEventStreamState.PollIntervalMs, cancellationToken);
            pollCount++;

            // Send a comment heartbeat every ~15 s to keep the connection alive
            // (browsers and proxies can close idle connections after ~30 s)
            if (pollCount % RunEventStreamState.HeartbeatEveryPolls == 0)
            {
                await Response.WriteAsync(":\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            var fresh = await streamState
                .BuildFreshEventsQuery(_dbContext.WorkflowEvents.AsNoTracking())
                .ToListAsync(cancellationToken);

            foreach (var evt in fresh)
            {
                await WriteSseEventAsync(evt, cancellationToken);
            }
            streamState.MarkDelivered(fresh);

            if (!RunEventStreamState.ShouldCheckRunStatus(fresh.Count))
            {
                continue;
            }

            var current = await _dbContext.WorkflowRuns
                .AsNoTracking()
                .Select(r => new { r.Id, r.Status })
                .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

            if (current == null)
            {
                await Response.WriteAsync("event: done\ndata: {}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                return;
            }

            if (Array.Exists(terminalStatuses, s => string.Equals(s, current.Status, StringComparison.Ordinal)))
            {
                var trailing = await streamState
                    .BuildFreshEventsQuery(_dbContext.WorkflowEvents.AsNoTracking())
                    .ToListAsync(cancellationToken);

                foreach (var evt in trailing)
                {
                    await WriteSseEventAsync(evt, cancellationToken);
                }
                streamState.MarkDelivered(trailing);

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

    [HttpGet("{runId}/interactions")]
    public async Task<IActionResult> ListInteractions(string runId, CancellationToken cancellationToken)
    {
        var interactions = await _dbContext.AgentInteractions
            .AsNoTracking()
            .Where(i => i.RunId == runId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(interactions.Select(ToInteractionSummary).ToList());
    }

    [Authorize(Policy = AgentwerkePolicies.Approver)]
    [HttpPost("{runId}/interactions/{interactionId}/answer")]
    public async Task<IActionResult> AnswerInteraction(
        string runId,
        string interactionId,
        [FromBody] AnswerInteractionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Answer))
        {
            return BadRequest(new { message = "An answer is required." });
        }

        try
        {
            var answeredBy = AuthenticatedPrincipal.ResolveSubject(User);
            var result = await _orchestrationService.AnswerInteractionAsync(
                new AnswerInteractionCommand(runId, interactionId, request.Answer, answeredBy),
                cancellationToken);

            return Accepted(result);
        }
        catch (InteractionNotFoundException)
        {
            return NotFound();
        }
        catch (InteractionNotPendingException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (WorkflowRunNotFoundException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
    }

    private static InteractionSummary ToInteractionSummary(AgentInteraction i) =>
        new(
            i.Id, i.RunId, i.StepId, i.FromAgent, i.Kind, i.AddresseeType, i.Addressee,
            i.Blocking, i.Prompt, i.Options, i.Status, i.Response, i.RespondedBy, i.RespondedAt, i.CreatedAt,
            i.ToolName, i.Intent);
}

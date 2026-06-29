using System.Text;
using System.Text.Json;
using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Autofac.Integrations;
using Autofac.Integrations.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofac.Api.Controllers;

/// <summary>
/// Inbound integration webhooks. Each endpoint validates the payload,
/// finds the active workflow tagged for the source, and starts a run.
/// </summary>
[ApiController]
[Route("webhooks")]
[AllowAnonymous]
public sealed class WebhooksController : ControllerBase
{
    private readonly IWorkflowRunOrchestrationService _orchestrationService;
    private readonly ITriggerRouter _triggerRouter;
    private readonly IExternalWorkflowEventRepository _externalEventRepository;
    private readonly IWaitingExternalCorrelationRepository _waitingExternalCorrelationRepository;
    private readonly IntegrationOptions _options;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IWorkflowRunOrchestrationService orchestrationService,
        ITriggerRouter triggerRouter,
        IExternalWorkflowEventRepository externalEventRepository,
        IWaitingExternalCorrelationRepository waitingExternalCorrelationRepository,
        IOptions<IntegrationOptions> options,
        ILogger<WebhooksController> logger)
    {
        _orchestrationService = orchestrationService;
        _triggerRouter = triggerRouter;
        _externalEventRepository = externalEventRepository;
        _waitingExternalCorrelationRepository = waitingExternalCorrelationRepository;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Accepts a Jira issue webhook and triggers the configured workflow.
    /// Workflows must carry the tag "jira-trigger" to be eligible.
    /// </summary>
    [HttpPost("jira")]
    public async Task<IActionResult> Jira(CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(cancellationToken);

        var validation = WebhookSignatureValidator.ValidateJira(
            body,
            Request.Headers["X-Hub-Signature"].FirstOrDefault(),
            _options.Jira.WebhookSecret);

        if (!validation.IsValid)
        {
            return Unauthorized(new { error = validation.ErrorMessage });
        }

        JiraWebhookPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<JiraWebhookPayload>(body)
                ?? throw new JsonException("Null payload.");
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid JSON payload.", detail = ex.Message });
        }

        if (!_options.Jira.TriggerEvents.Contains(
                payload.WebhookEvent, StringComparer.OrdinalIgnoreCase))
        {
            return Ok(new { skipped = true, reason = $"Event '{payload.WebhookEvent}' is not configured to trigger a run." });
        }

        if (payload.Issue is null)
        {
            return BadRequest(new { error = "Payload is missing 'issue' field." });
        }

        var workflowId = await _triggerRouter.ResolveWorkflowIdAsync("jira", cancellationToken);
        if (workflowId is null)
        {
            return UnprocessableEntity(new
            {
                error = "No active workflow with tag 'jira-trigger' found.",
                hint = "Add the tag 'jira-trigger' to an active workflow to enable this trigger."
            });
        }

        var trigger = new TriggerMetadata(
            Source: "jira",
            EventType: payload.WebhookEvent,
            ExternalId: payload.Issue.Key,
            ExternalUrl: payload.Issue.Self,
            Title: payload.Issue.Fields?.Summary,
            Body: payload.Issue.Fields?.Description,
            Inputs: BuildTriggerInputs(("issue_url", payload.Issue.Self)));

        var initiator = payload.User?.DisplayName ?? payload.User?.EmailAddress ?? "jira-webhook";

        return await StartRunAsync(workflowId, initiator, trigger, cancellationToken);
    }

    /// <summary>
    /// Accepts GitHub webhooks. "issues" triggers the configured workflow (workflows must
    /// carry the tag "github-trigger"). "pull_request", "workflow_run", and "check_suite"
    /// are normalized and recorded for the SDLC external-wait gates (#136), then matched
    /// against the waiting-external correlation store and auto-resumed if a run is waiting
    /// on that exact event kind + correlation key (#138).
    /// </summary>
    [HttpPost("github")]
    public async Task<IActionResult> GitHub(CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(cancellationToken);

        var validation = WebhookSignatureValidator.ValidateGitHub(
            body,
            Request.Headers["X-Hub-Signature-256"].FirstOrDefault(),
            _options.GitHub.WebhookSecret);

        if (!validation.IsValid)
        {
            return Unauthorized(new { error = validation.ErrorMessage });
        }

        var eventHeader = Request.Headers["X-GitHub-Event"].FirstOrDefault();
        return eventHeader?.ToLowerInvariant() switch
        {
            "issues" => await HandleIssuesEventAsync(body, cancellationToken),
            "pull_request" => await HandlePullRequestEventAsync(body, cancellationToken),
            "workflow_run" => await HandleWorkflowRunEventAsync(body, cancellationToken),
            "check_suite" => await HandleCheckSuiteEventAsync(body, cancellationToken),
            _ => Ok(new { skipped = true, reason = $"GitHub event '{eventHeader}' is not handled." })
        };
    }

    private async Task<IActionResult> HandleIssuesEventAsync(byte[] body, CancellationToken cancellationToken)
    {
        GitHubWebhookPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<GitHubWebhookPayload>(body)
                ?? throw new JsonException("Null payload.");
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid JSON payload.", detail = ex.Message });
        }

        if (!_options.GitHub.TriggerActions.Contains(
                payload.Action, StringComparer.OrdinalIgnoreCase))
        {
            return Ok(new { skipped = true, reason = $"Issue action '{payload.Action}' is not configured to trigger a run." });
        }

        if (payload.Issue is null)
        {
            return BadRequest(new { error = "Payload is missing 'issue' field." });
        }

        var workflowId = await _triggerRouter.ResolveWorkflowIdAsync("github", cancellationToken);
        if (workflowId is null)
        {
            return UnprocessableEntity(new
            {
                error = "No active workflow with tag 'github-trigger' found.",
                hint = "Add the tag 'github-trigger' to an active workflow to enable this trigger."
            });
        }

        var repo = payload.Repository?.FullName ?? "unknown";
        var trigger = new TriggerMetadata(
            Source: "github",
            EventType: $"issues.{payload.Action}",
            ExternalId: $"{repo}#{payload.Issue.Number}",
            ExternalUrl: payload.Issue.HtmlUrl,
            Title: payload.Issue.Title,
            Body: payload.Issue.Body,
            Inputs: BuildTriggerInputs(
                ("repository", payload.Repository?.FullName),
                ("issue_url", payload.Issue.HtmlUrl)));

        var initiator = payload.Sender?.Login ?? "github-webhook";

        return await StartRunAsync(workflowId, initiator, trigger, cancellationToken);
    }

    private async Task<IActionResult> HandlePullRequestEventAsync(byte[] body, CancellationToken cancellationToken)
    {
        GitHubPullRequestWebhookPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<GitHubPullRequestWebhookPayload>(body)
                ?? throw new JsonException("Null payload.");
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid JSON payload.", detail = ex.Message });
        }

        if (payload.PullRequest is null)
        {
            return BadRequest(new { error = "Payload is missing 'pull_request' field." });
        }

        var kind = payload.PullRequest.Merged
            ? "github.pull_request.merged"
            : $"github.pull_request.{payload.Action}";
        var correlationCandidates = BuildCorrelationCandidates(payload.PullRequest.Head?.Ref, payload.PullRequest.Head?.Sha);

        var @event = new ExternalWorkflowEvent
        {
            Id = $"ext_{Guid.NewGuid():N}",
            Kind = kind,
            CorrelationHint = correlationCandidates.Count > 0 ? correlationCandidates[0] : string.Empty,
            Payload = JsonSerializer.Serialize(new
            {
                action = payload.Action,
                number = payload.PullRequest.Number,
                merged = payload.PullRequest.Merged,
                mergeCommitSha = payload.PullRequest.MergeCommitSha,
                headRef = payload.PullRequest.Head?.Ref,
                headSha = payload.PullRequest.Head?.Sha,
                baseRef = payload.PullRequest.Base?.Ref,
            }),
            ReceivedAt = DateTimeOffset.UtcNow.ToString("o"),
        };

        var resumePayload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pr_number"] = payload.PullRequest.Number.ToString(),
            ["merged"] = payload.PullRequest.Merged ? "true" : "false",
        };
        if (payload.PullRequest.MergeCommitSha is { Length: > 0 } mergeCommitSha)
            resumePayload["merge_commit_sha"] = mergeCommitSha;
        if (payload.PullRequest.Head?.Sha is { Length: > 0 } headSha)
            resumePayload["head_sha"] = headSha;
        if (payload.PullRequest.Base?.Ref is { Length: > 0 } baseRef)
            resumePayload["base_ref"] = baseRef;

        return await RecordExternalEventAsync(@event, correlationCandidates, resumePayload, cancellationToken);
    }

    private async Task<IActionResult> HandleWorkflowRunEventAsync(byte[] body, CancellationToken cancellationToken)
    {
        GitHubWorkflowRunWebhookPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<GitHubWorkflowRunWebhookPayload>(body)
                ?? throw new JsonException("Null payload.");
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid JSON payload.", detail = ex.Message });
        }

        if (payload.WorkflowRun is null)
        {
            return BadRequest(new { error = "Payload is missing 'workflow_run' field." });
        }

        return await RecordRunStatusEventAsync("github.workflow_run", payload.Action, payload.WorkflowRun, cancellationToken);
    }

    private async Task<IActionResult> HandleCheckSuiteEventAsync(byte[] body, CancellationToken cancellationToken)
    {
        GitHubCheckSuiteWebhookPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<GitHubCheckSuiteWebhookPayload>(body)
                ?? throw new JsonException("Null payload.");
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid JSON payload.", detail = ex.Message });
        }

        if (payload.CheckSuite is null)
        {
            return BadRequest(new { error = "Payload is missing 'check_suite' field." });
        }

        return await RecordRunStatusEventAsync("github.check_suite", payload.Action, payload.CheckSuite, cancellationToken);
    }

    private async Task<IActionResult> RecordRunStatusEventAsync(
        string resourcePrefix,
        string action,
        GitHubRunStatus status,
        CancellationToken cancellationToken)
    {
        var correlationCandidates = BuildCorrelationCandidates(status.HeadBranch, status.HeadSha);

        var @event = new ExternalWorkflowEvent
        {
            Id = $"ext_{Guid.NewGuid():N}",
            Kind = $"{resourcePrefix}.{action}",
            CorrelationHint = correlationCandidates.Count > 0 ? correlationCandidates[0] : string.Empty,
            Payload = JsonSerializer.Serialize(new
            {
                action,
                status = status.Status,
                conclusion = status.Conclusion,
                headSha = status.HeadSha,
                headBranch = status.HeadBranch,
            }),
            ReceivedAt = DateTimeOffset.UtcNow.ToString("o"),
        };

        var resumePayload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = status.Status ?? string.Empty,
        };
        if (status.Conclusion is { Length: > 0 } conclusion)
            resumePayload["conclusion"] = conclusion;
        if (status.HeadSha is { Length: > 0 } headSha)
            resumePayload["head_sha"] = headSha;

        return await RecordExternalEventAsync(@event, correlationCandidates, resumePayload, cancellationToken);
    }

    /// <summary>
    /// A run may be waiting on either a branch name or a commit sha as its correlation key —
    /// e.g. "wait for PR merge" is naturally branch-keyed, while "wait for CI green" after a
    /// deploy dispatch (#139) is naturally sha-keyed, since a branch can have multiple runs.
    /// Branch is tried first (the common case), sha second. Empty/duplicate values are dropped.
    /// </summary>
    private static IReadOnlyList<string> BuildCorrelationCandidates(string? branch, string? sha)
    {
        var candidates = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(branch))
            candidates.Add(branch);
        if (!string.IsNullOrWhiteSpace(sha) && !string.Equals(sha, branch, StringComparison.Ordinal))
            candidates.Add(sha);

        return candidates;
    }

    private async Task<IActionResult> RecordExternalEventAsync(
        ExternalWorkflowEvent @event,
        IReadOnlyList<string> correlationCandidates,
        IReadOnlyDictionary<string, string> resumePayload,
        CancellationToken cancellationToken)
    {
        await _externalEventRepository.AddAsync(@event, cancellationToken);

        string? waitingRunId = null;
        string? matchedCorrelationKey = null;
        foreach (var candidate in correlationCandidates)
        {
            waitingRunId = await _waitingExternalCorrelationRepository.FindWaitingRunIdAsync(
                @event.Kind, candidate, cancellationToken);
            if (waitingRunId is not null)
            {
                matchedCorrelationKey = candidate;
                break;
            }
        }

        if (waitingRunId is null || matchedCorrelationKey is null)
        {
            _logger.LogDebug(
                "No run is waiting on external event {Kind} for any of [{Candidates}].",
                @event.Kind, string.Join(", ", correlationCandidates));
            return Ok(new { recorded = true, kind = @event.Kind, correlationHint = @event.CorrelationHint, resumed = false });
        }

        try
        {
            await _orchestrationService.ResumeExternalRunAsync(
                new ResumeExternalRunCommand(waitingRunId, matchedCorrelationKey, resumePayload, ResumedBy: "github-webhook"),
                cancellationToken);
        }
        catch (WorkflowRunNotFoundException)
        {
            _logger.LogWarning(
                "Waiting-external correlation pointed at run {RunId}, which no longer exists. Skipping auto-resume.", waitingRunId);
            return Ok(new { recorded = true, kind = @event.Kind, correlationHint = @event.CorrelationHint, resumed = false });
        }

        _logger.LogInformation(
            "Auto-resumed run {RunId} for external event {Kind}/{CorrelationKey}.",
            waitingRunId, @event.Kind, matchedCorrelationKey);

        return Ok(new { recorded = true, kind = @event.Kind, correlationHint = @event.CorrelationHint, resumed = true, runId = waitingRunId });
    }

    private async Task<IActionResult> StartRunAsync(
        string workflowId,
        string initiator,
        TriggerMetadata trigger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _orchestrationService.StartRunAsync(
                new StartRunCommand(workflowId, initiator, trigger),
                cancellationToken);

            return Accepted(new
            {
                runId = result.RunId,
                workflowId = result.WorkflowId,
                status = result.Status,
                trigger = new
                {
                    source = trigger.Source,
                    eventType = trigger.EventType,
                    externalId = trigger.ExternalId,
                    title = trigger.Title
                }
            });
        }
        catch (WorkflowNotFoundException)
        {
            return NotFound(new { error = $"Workflow '{workflowId}' not found." });
        }
        catch (WorkflowNotPublishedException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static IReadOnlyDictionary<string, string>? BuildTriggerInputs(params (string Key, string? Value)[] inputs)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in inputs)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                result[key] = value;
            }
        }

        return result.Count > 0 ? result : null;
    }

    private async Task<byte[]> ReadBodyAsync(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, cancellationToken);
        Request.Body.Position = 0;
        return ms.ToArray();
    }
}

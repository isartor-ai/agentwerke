using System.Text;
using System.Text.Json;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Agentwerke.Integrations;
using Agentwerke.Integrations.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentwerke.Api.Controllers;

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

        var fields = payload.Issue.Fields;
        var labels = fields?.Labels is { Count: > 0 } l ? string.Join(", ", l) : null;
        var trigger = new TriggerMetadata(
            Source: "jira",
            EventType: payload.WebhookEvent,
            ExternalId: payload.Issue.Key,
            ExternalUrl: payload.Issue.Self,
            Title: fields?.Summary,
            Body: fields?.Description,
            // Enrich run context with Jira ticket fields so agents/templates can
            // reference {{input.priority}}, {{input.issue_type}}, etc. (#30).
            Inputs: BuildTriggerInputs(
                ("issue_url", payload.Issue.Self),
                ("issue_key", payload.Issue.Key),
                ("issue_type", fields?.IssueType?.Name),
                ("project_key", fields?.Project?.Key),
                ("project_name", fields?.Project?.Name),
                ("priority", fields?.Priority?.Name),
                ("status", fields?.Status?.Name),
                ("labels", labels),
                ("assignee", fields?.Assignee?.DisplayName),
                ("reporter", fields?.Reporter?.DisplayName)));

        var initiator = payload.User?.DisplayName ?? payload.User?.EmailAddress ?? "jira-webhook";

        return await StartRunAsync(workflowId, initiator, trigger, cancellationToken);
    }

    /// <summary>
    /// Handles Slack interactivity callbacks: an approver clicking Approve/Reject on an
    /// approval notification (#172). The button value carries "{approvalId}:{runId}", so the
    /// decision is applied without a lookup. The request signature is verified with the Slack
    /// signing secret. Authorization is the shared-secret trust boundary; mapping the Slack
    /// user to an Agentwerke role is a follow-up.
    /// </summary>
    [HttpPost("slack/interactions")]
    public async Task<IActionResult> SlackInteractions(CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(cancellationToken);

        var validation = WebhookSignatureValidator.ValidateSlack(
            body,
            Request.Headers["X-Slack-Signature"].FirstOrDefault(),
            Request.Headers["X-Slack-Request-Timestamp"].FirstOrDefault(),
            _options.Slack.SigningSecret);
        if (!validation.IsValid)
        {
            return Unauthorized(new { error = validation.ErrorMessage });
        }

        var payloadJson = ExtractFormField(Encoding.UTF8.GetString(body), "payload");
        if (payloadJson is null)
        {
            return BadRequest(new { error = "Missing 'payload' field." });
        }

        SlackAction action;
        try
        {
            action = ParseSlackAction(payloadJson);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid interaction payload.", detail = ex.Message });
        }

        if (action.Decision is null)
        {
            return Ok(new { text = $"Ignored action '{action.ActionId}'." });
        }

        try
        {
            await _orchestrationService.ResumeRunAsync(
                new ResumeRunCommand(
                    RunId: action.RunId,
                    ApprovalId: action.ApprovalId,
                    Decision: action.Decision,
                    Comment: $"Decided via Slack by {action.User}.",
                    DecidedBy: $"slack:{action.User}"),
                cancellationToken);
        }
        catch (Exception ex) when (ex is ApprovalNotFoundException or ApprovalNotPendingException or WorkflowRunNotFoundException)
        {
            return Ok(new { replace_original = false, text = $"Could not apply decision: {ex.Message}" });
        }

        return Ok(new { replace_original = true, text = $":white_check_mark: Approval *{action.Decision}d* by {action.User}." });
    }

    private static string? ExtractFormField(string formBody, string field)
    {
        foreach (var pair in formBody.Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0 && pair[..separator] == field)
            {
                return System.Net.WebUtility.UrlDecode(pair[(separator + 1)..]);
            }
        }

        return null;
    }

    private static SlackAction ParseSlackAction(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        var firstAction = root.GetProperty("actions")[0];
        var actionId = firstAction.GetProperty("action_id").GetString() ?? string.Empty;
        var value = firstAction.TryGetProperty("value", out var v) ? v.GetString() ?? string.Empty : string.Empty;

        string user = "unknown";
        if (root.TryGetProperty("user", out var userEl))
        {
            user = (userEl.TryGetProperty("username", out var un) ? un.GetString() : null)
                ?? (userEl.TryGetProperty("id", out var id) ? id.GetString() : null)
                ?? "unknown";
        }

        var separator = value.IndexOf(':');
        var approvalId = separator > 0 ? value[..separator] : value;
        var runId = separator > 0 ? value[(separator + 1)..] : string.Empty;

        var decision = actionId switch
        {
            "approve" => "approve",
            "reject" => "reject",
            _ => null,
        };

        return new SlackAction(actionId, decision, approvalId, runId, user);
    }

    private sealed record SlackAction(string ActionId, string? Decision, string ApprovalId, string RunId, string User);

    /// <summary>
    /// Accepts GitHub webhooks. "issues" triggers the configured workflow (workflows must
    /// carry the tag "github-trigger") when the issue also carries the
    /// <see cref="GitHubOptions.RequiredLabel"/> label (#191). "issue_comment",
    /// "pull_request", "workflow_run", and "check_suite" are normalized and recorded for
    /// the SDLC external-wait gates (#136), then matched against the waiting-external
    /// correlation store and auto-resumed if a run is waiting on that exact event kind +
    /// correlation key (#138).
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
            "issue_comment" => await HandleIssueCommentEventAsync(body, cancellationToken),
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

        if (!HasRequiredLabel(payload.Issue.Labels, _options.GitHub.RequiredLabel))
        {
            return Ok(new
            {
                skipped = true,
                reason = $"Issue is missing required label '{_options.GitHub.RequiredLabel}'."
            });
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
                ("issue_url", payload.Issue.HtmlUrl),
                ("issue_number", payload.Issue.Number.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        var initiator = payload.Sender?.Login ?? "github-webhook";

        return await StartRunAsync(workflowId, initiator, trigger, cancellationToken);
    }

    /// <summary>
    /// An empty/whitespace <paramref name="requiredLabel"/> disables the check (opt-out,
    /// preserves pre-#191 behavior). Matching is case-insensitive.
    /// </summary>
    private static bool HasRequiredLabel(List<GitHubLabel>? labels, string requiredLabel)
    {
        if (string.IsNullOrWhiteSpace(requiredLabel))
        {
            return true;
        }

        if (labels is null)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (string.Equals(label.Name, requiredLabel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IActionResult> HandleIssueCommentEventAsync(byte[] body, CancellationToken cancellationToken)
    {
        GitHubIssueCommentWebhookPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<GitHubIssueCommentWebhookPayload>(body)
                ?? throw new JsonException("Null payload.");
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid JSON payload.", detail = ex.Message });
        }

        if (payload.Issue is null)
        {
            return BadRequest(new { error = "Payload is missing 'issue' field." });
        }

        if (payload.Comment is null)
        {
            return BadRequest(new { error = "Payload is missing 'comment' field." });
        }

        var approval = string.Equals(payload.Action, "created", StringComparison.OrdinalIgnoreCase)
            && ContainsApprovalToken(payload.Comment.Body);
        var kind = approval
            ? "github.issue_comment.approved"
            : $"github.issue_comment.{payload.Action}";
        var issueNumber = payload.Issue.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var repoIssue = string.IsNullOrWhiteSpace(payload.Repository?.FullName)
            ? null
            : $"{payload.Repository.FullName}#{issueNumber}";
        var correlationCandidates = BuildCorrelationCandidates(issueNumber, repoIssue);

        var @event = new ExternalWorkflowEvent
        {
            Id = $"ext_{Guid.NewGuid():N}",
            Kind = kind,
            CorrelationHint = issueNumber,
            Payload = JsonSerializer.Serialize(new
            {
                action = payload.Action,
                issueNumber = payload.Issue.Number,
                issueUrl = payload.Issue.HtmlUrl,
                commentId = payload.Comment.Id,
                commentUrl = payload.Comment.HtmlUrl,
                commentBody = payload.Comment.Body,
                commentAuthor = payload.Comment.User?.Login ?? payload.Sender?.Login,
                repository = payload.Repository?.FullName,
                approved = approval,
            }),
            ReceivedAt = DateTimeOffset.UtcNow.ToString("o"),
        };

        var resumePayload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["issue_number"] = issueNumber,
            ["approved"] = approval ? "true" : "false",
            ["comment_body"] = payload.Comment.Body ?? string.Empty,
        };
        if (payload.Comment.Id > 0)
            resumePayload["comment_id"] = payload.Comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (payload.Comment.HtmlUrl is { Length: > 0 } commentUrl)
            resumePayload["comment_url"] = commentUrl;
        var author = payload.Comment.User?.Login ?? payload.Sender?.Login;
        if (author is { Length: > 0 })
            resumePayload["comment_author"] = author;
        if (payload.Repository?.FullName is { Length: > 0 } repository)
            resumePayload["repository"] = repository;

        return await RecordExternalEventAsync(@event, correlationCandidates, resumePayload, cancellationToken);
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
    /// Generic signed event ingress (#206): lets any registered external system deliver a domain
    /// event that resumes a BPMN message wait, without Agentwerke needing a connector for it.
    /// The connector webhooks above speak GitHub's or Jira's vocabulary; this endpoint lets the
    /// sender name the message, so <c>test.unit.completed</c> is deliverable by whatever ran the tests.
    /// </summary>
    /// <remarks>
    /// Authenticated per-source by HMAC-SHA256 over the raw body (X-Agentwerke-Signature-256),
    /// keyed by X-Agentwerke-Source. Deduplicated on X-Agentwerke-Delivery, falling back to the
    /// body's signature digest so a sender that omits the header still cannot double-resume a run.
    /// </remarks>
    [HttpPost("events")]
    public async Task<IActionResult> Events(CancellationToken cancellationToken)
    {
        if (!_options.EventIngress.Enabled)
        {
            return NotFound(new { error = "Event ingress is not enabled." });
        }

        var body = await ReadBodyAsync(cancellationToken);
        var sourceId = Request.Headers["X-Agentwerke-Source"].FirstOrDefault();

        // A form/urlencoded content type makes ASP.NET consume the stream before we read it, so the
        // signature would be computed over an empty body and fail as a mismatch. Senders here are
        // third-party CI jobs; say what is actually wrong rather than blaming their secret.
        if (body.Length == 0)
        {
            return BadRequest(new
            {
                error = "Empty request body.",
                hint = "Send the event as Content-Type: application/json.",
            });
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return Unauthorized(new { error = "Missing X-Agentwerke-Source header." });
        }

        var source = _options.EventIngress.Sources
            .FirstOrDefault(s => string.Equals(s.Id, sourceId, StringComparison.OrdinalIgnoreCase));

        // An unregistered source and a bad signature get the same answer on purpose: the caller
        // learns only that it is not authorized, not which source ids exist.
        if (source is null)
        {
            _logger.LogWarning("Rejected event ingress from unregistered source '{Source}'.", sourceId);
            return Unauthorized(new { error = "Unknown or unauthorized event source." });
        }

        var validation = WebhookSignatureValidator.ValidateEventIngress(
            body,
            Request.Headers["X-Agentwerke-Signature-256"].FirstOrDefault(),
            source.Secret);

        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "Rejected event ingress from source '{Source}': {Reason}", source.Id, validation.ErrorMessage);
            return Unauthorized(new { error = validation.ErrorMessage });
        }

        EventIngressPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<EventIngressPayload>(body, EventIngressSerializerOptions)
                ?? throw new JsonException("Null payload.");
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid JSON payload.", detail = ex.Message });
        }

        if (string.IsNullOrWhiteSpace(payload.MessageName))
        {
            return BadRequest(new { error = "Payload is missing 'messageName'." });
        }

        if (string.IsNullOrWhiteSpace(payload.CorrelationKey))
        {
            return BadRequest(new { error = "Payload is missing 'correlationKey'." });
        }

        if (source.AllowedMessageNames.Count > 0
            && !source.AllowedMessageNames.Contains(payload.MessageName, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Source '{Source}' is not allowed to deliver message '{MessageName}'.", source.Id, payload.MessageName);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = $"Source '{source.Id}' is not allowed to deliver message '{payload.MessageName}'."
            });
        }

        // An explicit delivery id lets a sender retry a failed POST without re-resuming. Without one,
        // the signature digest identifies the body, which for a correlated domain event is the event.
        var deliveryId = Request.Headers["X-Agentwerke-Delivery"].FirstOrDefault() is { Length: > 0 } header
            ? $"{source.Id}:{header}"
            : $"{source.Id}:{WebhookSignatureValidator.ComputeSignatureDigest(body, source.Secret)}";

        var @event = new ExternalWorkflowEvent
        {
            Id = $"ext_{Guid.NewGuid():N}",
            Kind = payload.MessageName,
            CorrelationHint = payload.CorrelationKey,
            Payload = payload.Payload is { ValueKind: not JsonValueKind.Undefined } p
                ? p.GetRawText()
                : "{}",
            ReceivedAt = DateTimeOffset.UtcNow.ToString("o"),
            Source = source.Id,
            DeliveryId = deliveryId,
        };

        if (!await _externalEventRepository.TryAddAsync(@event, cancellationToken))
        {
            _logger.LogInformation(
                "Ignored duplicate event delivery {DeliveryId} for {MessageName}.", deliveryId, payload.MessageName);
            return Ok(new
            {
                recorded = true,
                duplicate = true,
                kind = @event.Kind,
                correlationHint = @event.CorrelationHint,
                resumed = false,
            });
        }

        return await MatchAndResumeAsync(
            @event,
            [payload.CorrelationKey],
            FlattenResumePayload(payload.Payload),
            $"event-ingress:{source.Id}",
            cancellationToken);
    }

    /// <summary>
    /// Run inputs are a string map, so the event's JSON payload is flattened one level: scalars
    /// become their text, and nested objects/arrays keep their raw JSON so nothing is silently
    /// dropped from the evidence a resumed run sees.
    /// </summary>
    private static IReadOnlyDictionary<string, string> FlattenResumePayload(JsonElement payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in payload.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => property.Value.GetRawText(),
            };
        }

        return result;
    }

    private static readonly JsonSerializerOptions EventIngressSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record EventIngressPayload(
        string? MessageName,
        string? CorrelationKey,
        JsonElement Payload);

    /// <summary>
    /// A run may be waiting on either a branch name or a commit sha as its correlation key —
    /// e.g. "wait for PR merge" is naturally branch-keyed, while "wait for CI green" after a
    /// deploy dispatch (#139) is naturally sha-keyed, since a branch can have multiple runs.
    /// Branch is tried first (the common case), sha second. Empty/duplicate values are dropped.
    /// </summary>
    private static bool ContainsApprovalToken(string? commentBody)
    {
        if (string.IsNullOrWhiteSpace(commentBody))
        {
            return false;
        }

        return commentBody
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(static line =>
                string.Equals(line, "approved", StringComparison.OrdinalIgnoreCase)
                || string.Equals(line, "/approve", StringComparison.OrdinalIgnoreCase)
                || line.Contains("agentwerke approved", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> BuildCorrelationCandidates(string? primary, string? secondary)
    {
        var candidates = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(primary))
            candidates.Add(primary);
        if (!string.IsNullOrWhiteSpace(secondary) && !string.Equals(secondary, primary, StringComparison.Ordinal))
            candidates.Add(secondary);

        return candidates;
    }

    private async Task<IActionResult> RecordExternalEventAsync(
        ExternalWorkflowEvent @event,
        IReadOnlyList<string> correlationCandidates,
        IReadOnlyDictionary<string, string> resumePayload,
        CancellationToken cancellationToken)
    {
        await _externalEventRepository.AddAsync(@event, cancellationToken);
        return await MatchAndResumeAsync(@event, correlationCandidates, resumePayload, "github-webhook", cancellationToken);
    }

    /// <summary>
    /// Finds a run waiting on this event's message name and any of the correlation candidates, and
    /// resumes it. Shared by the connector webhooks and the generic event ingress (#206) — the
    /// matching is already vocabulary-agnostic, so only the callers differ.
    /// </summary>
    private async Task<IActionResult> MatchAndResumeAsync(
        ExternalWorkflowEvent @event,
        IReadOnlyList<string> correlationCandidates,
        IReadOnlyDictionary<string, string> resumePayload,
        string resumedBy,
        CancellationToken cancellationToken)
    {
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
                new ResumeExternalRunCommand(waitingRunId, matchedCorrelationKey, resumePayload, ResumedBy: resumedBy),
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

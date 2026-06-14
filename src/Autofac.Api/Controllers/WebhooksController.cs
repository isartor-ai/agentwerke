using System.Text;
using System.Text.Json;
using Autofac.Application.Workflows;
using Autofac.Integrations;
using Autofac.Integrations.Webhooks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Autofac.Api.Controllers;

/// <summary>
/// Inbound integration webhooks. Each endpoint validates the payload,
/// finds the active workflow tagged for the source, and starts a run.
/// </summary>
[ApiController]
[Route("webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly IWorkflowRunOrchestrationService _orchestrationService;
    private readonly ITriggerRouter _triggerRouter;
    private readonly IntegrationOptions _options;

    public WebhooksController(
        IWorkflowRunOrchestrationService orchestrationService,
        ITriggerRouter triggerRouter,
        IOptions<IntegrationOptions> options)
    {
        _orchestrationService = orchestrationService;
        _triggerRouter = triggerRouter;
        _options = options.Value;
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
            Body: payload.Issue.Fields?.Description);

        var initiator = payload.User?.DisplayName ?? payload.User?.EmailAddress ?? "jira-webhook";

        return await StartRunAsync(workflowId, initiator, trigger, cancellationToken);
    }

    /// <summary>
    /// Accepts a GitHub "issues" webhook and triggers the configured workflow.
    /// Workflows must carry the tag "github-trigger" to be eligible.
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
        if (!string.Equals(eventHeader, "issues", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new { skipped = true, reason = $"GitHub event '{eventHeader}' is not the 'issues' event." });
        }

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
            Body: payload.Issue.Body);

        var initiator = payload.Sender?.Login ?? "github-webhook";

        return await StartRunAsync(workflowId, initiator, trigger, cancellationToken);
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

    private async Task<byte[]> ReadBodyAsync(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, cancellationToken);
        Request.Body.Position = 0;
        return ms.ToArray();
    }
}

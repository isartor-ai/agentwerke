using System.Text;
using Autofac.Api.Controllers;
using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Autofac.Integrations;
using Autofac.Integrations.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autofac.Api.Tests;

public sealed class WebhooksControllerTests
{
    [Fact]
    public async Task GitHub_PullRequestMergedEvent_RecordsExternalWorkflowEventWithoutStartingARun()
    {
        var orchestration = new CapturingOrchestrationService();
        var eventRepository = new CapturingExternalWorkflowEventRepository();
        var controller = CreateController(orchestration, eventRepository, eventHeader: "pull_request");

        var body = """
            {
              "action": "closed",
              "pull_request": {
                "number": 42,
                "html_url": "https://github.com/octo/autofac/pull/42",
                "merged": true,
                "merge_commit_sha": "feedface1234",
                "head": { "ref": "autofac/run-123", "sha": "abc123" },
                "base": { "ref": "main", "sha": "def456" }
              },
              "repository": { "full_name": "octo/autofac" }
            }
            """;
        SetBody(controller, body);

        var result = await controller.GitHub(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(orchestration.StartCommand);
        Assert.Null(orchestration.ResumeExternalCommand);

        var recorded = Assert.Single(eventRepository.Added);
        Assert.Equal("github.pull_request.merged", recorded.Kind);
        Assert.Equal("autofac/run-123", recorded.CorrelationHint);
        Assert.Contains("\"merged\":true", recorded.Payload, StringComparison.Ordinal);
        Assert.Contains("\"mergeCommitSha\":\"feedface1234\"", recorded.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHub_PullRequestMergedEvent_WhenAWaitingRunMatchesTheBranch_AutoResumesIt()
    {
        var orchestration = new CapturingOrchestrationService();
        var eventRepository = new CapturingExternalWorkflowEventRepository();
        var waitingRepository = new StubWaitingExternalCorrelationRepository(runId: "run_waiting_123");
        var controller = CreateController(
            orchestration,
            eventRepository,
            eventHeader: "pull_request",
            waitingExternalCorrelationRepository: waitingRepository);

        var body = """
            {
              "action": "closed",
              "pull_request": {
                "number": 42,
                "merged": true,
                "merge_commit_sha": "feedface1234",
                "head": { "ref": "autofac/run-waiting-123", "sha": "abc123" },
                "base": { "ref": "main", "sha": "def456" }
              }
            }
            """;
        SetBody(controller, body);

        var result = await controller.GitHub(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(orchestration.ResumeExternalCommand);
        Assert.Equal("run_waiting_123", orchestration.ResumeExternalCommand!.RunId);
        Assert.Equal("autofac/run-waiting-123", orchestration.ResumeExternalCommand.CorrelationKey);
        Assert.Equal("github-webhook", orchestration.ResumeExternalCommand.ResumedBy);
        Assert.Equal("42", orchestration.ResumeExternalCommand.Payload["pr_number"]);
        Assert.Equal("feedface1234", orchestration.ResumeExternalCommand.Payload["merge_commit_sha"]);
    }

    [Fact]
    public async Task GitHub_WorkflowRunCompletedEvent_WhenAWaitingRunMatchesTheBranch_AutoResumesIt()
    {
        var orchestration = new CapturingOrchestrationService();
        var eventRepository = new CapturingExternalWorkflowEventRepository();
        var waitingRepository = new StubWaitingExternalCorrelationRepository(runId: "run_waiting_456");
        var controller = CreateController(
            orchestration,
            eventRepository,
            eventHeader: "workflow_run",
            waitingExternalCorrelationRepository: waitingRepository);

        var body = """
            {
              "action": "completed",
              "workflow_run": {
                "status": "completed",
                "conclusion": "success",
                "head_sha": "abc123",
                "head_branch": "autofac/run-waiting-456"
              }
            }
            """;
        SetBody(controller, body);

        var result = await controller.GitHub(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(orchestration.ResumeExternalCommand);
        Assert.Equal("run_waiting_456", orchestration.ResumeExternalCommand!.RunId);
        Assert.Equal("autofac/run-waiting-456", orchestration.ResumeExternalCommand.CorrelationKey);
        Assert.Equal("success", orchestration.ResumeExternalCommand.Payload["conclusion"]);
    }

    [Fact]
    public async Task GitHub_PullRequestMergedEvent_ForAnUnrelatedBranch_DoesNotResumeAnyRun()
    {
        var orchestration = new CapturingOrchestrationService();
        var eventRepository = new CapturingExternalWorkflowEventRepository();
        var waitingRepository = new StubWaitingExternalCorrelationRepository(runId: null);
        var controller = CreateController(
            orchestration,
            eventRepository,
            eventHeader: "pull_request",
            waitingExternalCorrelationRepository: waitingRepository);

        var body = """
            {
              "action": "closed",
              "pull_request": {
                "number": 99,
                "merged": true,
                "head": { "ref": "some-other-branch", "sha": "zzz999" },
                "base": { "ref": "main", "sha": "def456" }
              }
            }
            """;
        SetBody(controller, body);

        var result = await controller.GitHub(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(orchestration.ResumeExternalCommand);
        Assert.Single(eventRepository.Added);
    }

    [Fact]
    public async Task GitHub_PullRequestOpenedEvent_RecordsActionSpecificKind()
    {
        var eventRepository = new CapturingExternalWorkflowEventRepository();
        var controller = CreateController(new CapturingOrchestrationService(), eventRepository, eventHeader: "pull_request");

        var body = """
            {
              "action": "opened",
              "pull_request": {
                "number": 7,
                "merged": false,
                "head": { "ref": "feature/x", "sha": "sha1" },
                "base": { "ref": "main", "sha": "sha2" }
              }
            }
            """;
        SetBody(controller, body);

        await controller.GitHub(CancellationToken.None);

        var recorded = Assert.Single(eventRepository.Added);
        Assert.Equal("github.pull_request.opened", recorded.Kind);
        Assert.Equal("feature/x", recorded.CorrelationHint);
    }

    [Fact]
    public async Task GitHub_WorkflowRunCompletedEvent_RecordsExternalWorkflowEvent()
    {
        var eventRepository = new CapturingExternalWorkflowEventRepository();
        var controller = CreateController(new CapturingOrchestrationService(), eventRepository, eventHeader: "workflow_run");

        var body = """
            {
              "action": "completed",
              "workflow_run": {
                "status": "completed",
                "conclusion": "success",
                "head_sha": "abc123",
                "head_branch": "autofac/run-123"
              },
              "repository": { "full_name": "octo/autofac" }
            }
            """;
        SetBody(controller, body);

        var result = await controller.GitHub(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);

        var recorded = Assert.Single(eventRepository.Added);
        Assert.Equal("github.workflow_run.completed", recorded.Kind);
        Assert.Equal("autofac/run-123", recorded.CorrelationHint);
        Assert.Contains("\"conclusion\":\"success\"", recorded.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHub_CheckSuiteCompletedEvent_RecordsExternalWorkflowEvent()
    {
        var eventRepository = new CapturingExternalWorkflowEventRepository();
        var controller = CreateController(new CapturingOrchestrationService(), eventRepository, eventHeader: "check_suite");

        var body = """
            {
              "action": "completed",
              "check_suite": {
                "status": "completed",
                "conclusion": "failure",
                "head_sha": "abc123",
                "head_branch": "autofac/run-123"
              }
            }
            """;
        SetBody(controller, body);

        await controller.GitHub(CancellationToken.None);

        var recorded = Assert.Single(eventRepository.Added);
        Assert.Equal("github.check_suite.completed", recorded.Kind);
        Assert.Equal("failure", System.Text.Json.JsonDocument.Parse(recorded.Payload).RootElement.GetProperty("conclusion").GetString());
    }

    [Fact]
    public async Task GitHub_UnhandledEventType_IsSkippedWithoutRecordingOrStartingARun()
    {
        var orchestration = new CapturingOrchestrationService();
        var eventRepository = new CapturingExternalWorkflowEventRepository();
        var controller = CreateController(orchestration, eventRepository, eventHeader: "push");
        SetBody(controller, "{}");

        var result = await controller.GitHub(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(eventRepository.Added);
        Assert.Null(orchestration.StartCommand);
    }

    [Fact]
    public async Task GitHub_IssuesEventStillTriggersARun()
    {
        var orchestration = new CapturingOrchestrationService();
        var eventRepository = new CapturingExternalWorkflowEventRepository();
        var controller = CreateController(
            orchestration,
            eventRepository,
            eventHeader: "issues",
            triggerRouter: new StubTriggerRouter("wf_1"));

        var body = """
            {
              "action": "opened",
              "issue": { "number": 5, "html_url": "https://github.com/octo/autofac/issues/5", "title": "Idea", "body": "Do the thing", "state": "open" },
              "repository": { "full_name": "octo/autofac" },
              "sender": { "login": "alice" }
            }
            """;
        SetBody(controller, body);

        var result = await controller.GitHub(CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.NotNull(orchestration.StartCommand);
        Assert.Equal("wf_1", orchestration.StartCommand!.WorkflowId);
        Assert.Empty(eventRepository.Added);
    }

    [Fact]
    public async Task GitHub_IssuesEvent_PassesRepositoryAndIssueUrlAsTriggerInputs()
    {
        var orchestration = new CapturingOrchestrationService();
        var controller = CreateController(
            orchestration,
            new CapturingExternalWorkflowEventRepository(),
            eventHeader: "issues",
            triggerRouter: new StubTriggerRouter("wf_1"));

        var body = """
            {
              "action": "opened",
              "issue": { "number": 142, "html_url": "https://github.com/isartor-ai/autofac/issues/142", "title": "Seed inputs", "body": "Do the thing", "state": "open" },
              "repository": { "full_name": "isartor-ai/autofac" },
              "sender": { "login": "alice" }
            }
            """;
        SetBody(controller, body);

        var result = await controller.GitHub(CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.NotNull(orchestration.StartCommand?.Trigger?.Inputs);
        Assert.Equal("isartor-ai/autofac", orchestration.StartCommand!.Trigger!.Inputs!["repository"]);
        Assert.Equal(
            "https://github.com/isartor-ai/autofac/issues/142",
            orchestration.StartCommand.Trigger.Inputs["issue_url"]);
    }

    [Fact]
    public async Task Jira_IssueCreated_SeedsEnrichedTicketContextAsInputs()
    {
        var orchestration = new CapturingOrchestrationService();
        var controller = CreateController(
            orchestration,
            new CapturingExternalWorkflowEventRepository(),
            eventHeader: "issues",
            triggerRouter: new StubTriggerRouter("wf_1"));

        var body = """
            {
              "webhookEvent": "jira:issue_created",
              "issue": {
                "id": "10001",
                "key": "ENG-42",
                "self": "https://acme.atlassian.net/rest/api/2/issue/10001",
                "fields": {
                  "summary": "Add dark mode",
                  "description": "As a user I want dark mode.",
                  "issuetype": { "name": "Story" },
                  "project": { "key": "ENG", "name": "Engineering" },
                  "priority": { "name": "High" },
                  "status": { "name": "To Do" },
                  "labels": ["frontend", "ux"],
                  "assignee": { "displayName": "Alice Dev" },
                  "reporter": { "displayName": "Bob PM" }
                }
              },
              "user": { "displayName": "Bob PM" }
            }
            """;
        SetBody(controller, body);

        var result = await controller.Jira(CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        var trigger = orchestration.StartCommand?.Trigger;
        Assert.NotNull(trigger?.Inputs);
        Assert.Equal("Add dark mode", trigger!.Title);
        Assert.Equal("As a user I want dark mode.", trigger.Body);
        var inputs = trigger.Inputs!;
        Assert.Equal("ENG-42", inputs["issue_key"]);
        Assert.Equal("Story", inputs["issue_type"]);
        Assert.Equal("ENG", inputs["project_key"]);
        Assert.Equal("Engineering", inputs["project_name"]);
        Assert.Equal("High", inputs["priority"]);
        Assert.Equal("To Do", inputs["status"]);
        Assert.Equal("frontend, ux", inputs["labels"]);
        Assert.Equal("Alice Dev", inputs["assignee"]);
        Assert.Equal("Bob PM", inputs["reporter"]);
        Assert.Equal("https://acme.atlassian.net/rest/api/2/issue/10001", inputs["issue_url"]);
    }

    [Fact]
    public async Task GitHub_WorkflowRunCompletedEvent_WhenOnlyTheCommitShaMatchesAWaitingRun_StillAutoResumesIt()
    {
        // Simulates the #139 "wait for CI green after a deploy dispatch" gate: the message-catch
        // node was keyed on the commit sha (not the branch), since a branch can have many runs.
        var orchestration = new CapturingOrchestrationService();
        var eventRepository = new CapturingExternalWorkflowEventRepository();
        var waitingRepository = new StubWaitingExternalCorrelationRepository(runId: "run_sha_match", onlyMatchKey: "sha789");
        var controller = CreateController(
            orchestration,
            eventRepository,
            eventHeader: "workflow_run",
            waitingExternalCorrelationRepository: waitingRepository);

        var body = """
            {
              "action": "completed",
              "workflow_run": {
                "status": "completed",
                "conclusion": "success",
                "head_sha": "sha789",
                "head_branch": "main"
              }
            }
            """;
        SetBody(controller, body);

        var result = await controller.GitHub(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(orchestration.ResumeExternalCommand);
        Assert.Equal("run_sha_match", orchestration.ResumeExternalCommand!.RunId);
        Assert.Equal("sha789", orchestration.ResumeExternalCommand.CorrelationKey);
    }

    [Fact]
    public async Task SlackInteractions_ValidSignature_AppliesApprovalDecision()
    {
        var orchestration = new CapturingOrchestrationService();
        const string secret = "slack-signing-secret";
        var controller = CreateController(
            orchestration, new CapturingExternalWorkflowEventRepository(), eventHeader: "", slackSigningSecret: secret);

        var json = "{\"actions\":[{\"action_id\":\"approve\",\"value\":\"apr_1:run_42\"}],\"user\":{\"username\":\"alice\"}}";
        SetSignedBody(controller, "payload=" + Uri.EscapeDataString(json), secret);

        var result = await controller.SlackInteractions(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(orchestration.ResumeCommand);
        Assert.Equal("run_42", orchestration.ResumeCommand!.RunId);
        Assert.Equal("apr_1", orchestration.ResumeCommand.ApprovalId);
        Assert.Equal("approve", orchestration.ResumeCommand.Decision);
        Assert.Contains("alice", orchestration.ResumeCommand.DecidedBy!);
    }

    [Fact]
    public async Task SlackInteractions_BadSignature_ReturnsUnauthorized()
    {
        var orchestration = new CapturingOrchestrationService();
        var controller = CreateController(
            orchestration, new CapturingExternalWorkflowEventRepository(), eventHeader: "", slackSigningSecret: "secret");

        var bytes = Encoding.UTF8.GetBytes("payload=%7B%7D");
        var request = controller.ControllerContext.HttpContext.Request;
        request.Body = new MemoryStream(bytes);
        request.Headers["X-Slack-Signature"] = "v0=deadbeef";
        request.Headers["X-Slack-Request-Timestamp"] = "1700000000";

        var result = await controller.SlackInteractions(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Null(orchestration.ResumeCommand);
    }

    private static void SetSignedBody(WebhooksController controller, string rawBody, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(rawBody);
        const string ts = "1700000000";
        var basestring = Encoding.UTF8.GetBytes($"v0:{ts}:").Concat(bytes).ToArray();
        var hash = System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), basestring);
        var request = controller.ControllerContext.HttpContext.Request;
        request.Body = new MemoryStream(bytes);
        request.ContentLength = bytes.Length;
        request.Headers["X-Slack-Signature"] = "v0=" + Convert.ToHexString(hash).ToLowerInvariant();
        request.Headers["X-Slack-Request-Timestamp"] = ts;
    }

    private static WebhooksController CreateController(
        IWorkflowRunOrchestrationService orchestration,
        IExternalWorkflowEventRepository eventRepository,
        string eventHeader,
        ITriggerRouter? triggerRouter = null,
        IWaitingExternalCorrelationRepository? waitingExternalCorrelationRepository = null,
        string? slackSigningSecret = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-GitHub-Event"] = eventHeader;

        return new WebhooksController(
            orchestration,
            triggerRouter ?? new StubTriggerRouter(null),
            eventRepository,
            waitingExternalCorrelationRepository ?? new StubWaitingExternalCorrelationRepository(runId: null),
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    Enabled = true,
                    WebhookSecret = string.Empty,
                    TriggerActions = ["opened"],
                },
                Slack = new SlackOptions
                {
                    SigningSecret = slackSigningSecret ?? string.Empty,
                },
            }),
            NullLogger<WebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static void SetBody(WebhooksController controller, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        controller.ControllerContext.HttpContext.Request.Body = new MemoryStream(bytes);
        controller.ControllerContext.HttpContext.Request.ContentLength = bytes.Length;
    }

    private sealed class CapturingOrchestrationService : IWorkflowRunOrchestrationService
    {
        public StartRunCommand? StartCommand { get; private set; }

        public ResumeExternalRunCommand? ResumeExternalCommand { get; private set; }

        public ResumeRunCommand? ResumeCommand { get; private set; }

        public Task<StartRunResult> StartRunAsync(StartRunCommand command, CancellationToken cancellationToken = default)
        {
            StartCommand = command;
            return Task.FromResult(new StartRunResult("run_1", command.WorkflowId, "pending", null));
        }

        public Task<ResumeRunResult> ResumeRunAsync(ResumeRunCommand command, CancellationToken cancellationToken = default)
        {
            ResumeCommand = command;
            return Task.FromResult(new ResumeRunResult(command.RunId, "running", null));
        }

        public Task<ResumeExternalRunResult> ResumeExternalRunAsync(ResumeExternalRunCommand command, CancellationToken cancellationToken = default)
        {
            ResumeExternalCommand = command;
            return Task.FromResult(new ResumeExternalRunResult(command.RunId, "pending"));
        }

        public Task<RecoverRunResult> RecoverRunAsync(string runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingExternalWorkflowEventRepository : IExternalWorkflowEventRepository
    {
        public List<ExternalWorkflowEvent> Added { get; } = [];

        public Task AddAsync(ExternalWorkflowEvent @event, CancellationToken cancellationToken)
        {
            Added.Add(@event);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Matches any (messageName, correlationKey) lookup when constructed with a flat run id.
    /// Construct with <paramref name="onlyMatchKey"/> to simulate a run waiting on one *specific*
    /// correlation key (e.g. a commit sha) so multi-candidate lookups (#139) can be exercised.
    /// </summary>
    private sealed class StubWaitingExternalCorrelationRepository(string? runId, string? onlyMatchKey = null) : IWaitingExternalCorrelationRepository
    {
        public Task UpsertAsync(WaitingExternalCorrelation correlation, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> FindWaitingRunIdAsync(string messageName, string correlationKey, CancellationToken cancellationToken) =>
            Task.FromResult(onlyMatchKey is null || string.Equals(onlyMatchKey, correlationKey, StringComparison.Ordinal) ? runId : null);
    }

    private sealed class StubTriggerRouter(string? workflowId) : ITriggerRouter
    {
        public Task<string?> ResolveWorkflowIdAsync(string triggerSource, CancellationToken cancellationToken) =>
            Task.FromResult(workflowId);
    }
}

using System.Text;
using Autofac.Api.Controllers;
using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Autofac.Integrations;
using Autofac.Integrations.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

        var recorded = Assert.Single(eventRepository.Added);
        Assert.Equal("github.pull_request.merged", recorded.Kind);
        Assert.Equal("autofac/run-123", recorded.CorrelationHint);
        Assert.Contains("\"merged\":true", recorded.Payload, StringComparison.Ordinal);
        Assert.Contains("\"mergeCommitSha\":\"feedface1234\"", recorded.Payload, StringComparison.Ordinal);
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

    private static WebhooksController CreateController(
        IWorkflowRunOrchestrationService orchestration,
        IExternalWorkflowEventRepository eventRepository,
        string eventHeader,
        ITriggerRouter? triggerRouter = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-GitHub-Event"] = eventHeader;

        return new WebhooksController(
            orchestration,
            triggerRouter ?? new StubTriggerRouter(null),
            eventRepository,
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    Enabled = true,
                    WebhookSecret = string.Empty,
                    TriggerActions = ["opened"],
                },
            }))
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

        public Task<StartRunResult> StartRunAsync(StartRunCommand command, CancellationToken cancellationToken = default)
        {
            StartCommand = command;
            return Task.FromResult(new StartRunResult("run_1", command.WorkflowId, "pending", null));
        }

        public Task<ResumeRunResult> ResumeRunAsync(ResumeRunCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

    private sealed class StubTriggerRouter(string? workflowId) : ITriggerRouter
    {
        public Task<string?> ResolveWorkflowIdAsync(string triggerSource, CancellationToken cancellationToken) =>
            Task.FromResult(workflowId);
    }
}

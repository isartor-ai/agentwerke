using System.Net;
using Agentwerke.Application.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentwerke.Integrations.Tests;

public sealed class ApprovalNotifierTests
{
    private static readonly ApprovalNotification Sample = new(
        RunId: "run_1",
        ApprovalId: "apr_1",
        WorkflowName: "Deploy",
        ActionRequested: "deploy",
        RiskLevel: "high",
        ArtifactName: "plan.md");

    [Fact]
    public async Task Notifies_BothChannels_WhenEnabled()
    {
        var urls = new List<string?>();
        var options = new IntegrationOptions
        {
            Slack = new SlackOptions { Enabled = true, WebhookUrl = "https://hooks.slack.test/1" },
            Teams = new TeamsOptions { Enabled = true, WebhookUrl = "https://teams.test/1" },
        };
        var notifier = BuildNotifier(options, urls);

        await notifier.NotifyApprovalRequestedAsync(Sample);

        Assert.Equal(2, urls.Count);
        Assert.Contains("https://hooks.slack.test/1", urls);
        Assert.Contains("https://teams.test/1", urls);
    }

    [Fact]
    public async Task Skips_DisabledChannel()
    {
        var urls = new List<string?>();
        var options = new IntegrationOptions
        {
            Slack = new SlackOptions { Enabled = true, WebhookUrl = "https://hooks.slack.test/1" },
            Teams = new TeamsOptions { Enabled = false, WebhookUrl = "https://teams.test/1" },
        };
        var notifier = BuildNotifier(options, urls);

        await notifier.NotifyApprovalRequestedAsync(Sample);

        Assert.Equal(["https://hooks.slack.test/1"], urls);
    }

    [Fact]
    public async Task Sends_Nothing_WhenApprovalNotificationsDisabled()
    {
        var urls = new List<string?>();
        var options = new IntegrationOptions
        {
            Slack = new SlackOptions { Enabled = true, WebhookUrl = "https://hooks.slack.test/1" },
            Notifications = new NotificationOptions { OnApprovalRequested = false },
        };
        var notifier = BuildNotifier(options, urls);

        await notifier.NotifyApprovalRequestedAsync(Sample);

        Assert.Empty(urls);
    }

    [Fact]
    public async Task DoesNotThrow_WhenAChannelFails()
    {
        var urls = new List<string?>();
        var options = new IntegrationOptions
        {
            Slack = new SlackOptions { Enabled = true, WebhookUrl = "https://hooks.slack.test/1" },
            Teams = new TeamsOptions { Enabled = true, WebhookUrl = "https://teams.test/1" },
        };
        // Slack returns 500; Teams still succeeds and the call completes.
        var notifier = BuildNotifier(options, urls, failHost: "hooks.slack.test");

        var ex = await Record.ExceptionAsync(() => notifier.NotifyApprovalRequestedAsync(Sample));

        Assert.Null(ex);
        Assert.Contains("https://teams.test/1", urls);
    }

    private static ConnectorApprovalNotifier BuildNotifier(
        IntegrationOptions options,
        List<string?> capturedUrls,
        string? failHost = null)
    {
        var opts = Options.Create(options);

        HttpClient Client() => new(new RecordingHandler(capturedUrls, failHost));

        var slack = new SlackConnector(
            Client(), opts, new StubSecretStore(), new AllowAllPolicyEvaluationService(),
            new NoOpAuditRepository(), new NoOpWorkflowMetrics(), new StubCorrelationContext(), new NoOpWorkflowTracer());
        var teams = new TeamsConnector(
            Client(), opts, new StubSecretStore(), new AllowAllPolicyEvaluationService(),
            new NoOpAuditRepository(), new NoOpWorkflowMetrics(), new StubCorrelationContext(), new NoOpWorkflowTracer());

        return new ConnectorApprovalNotifier(slack, teams, opts, NullLogger<ConnectorApprovalNotifier>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<string?> _urls;
        private readonly string? _failHost;

        public RecordingHandler(List<string?> urls, string? failHost)
        {
            _urls = urls;
            _failHost = failHost;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _urls.Add(request.RequestUri?.ToString());
            var status = _failHost is not null && request.RequestUri?.Host == _failHost
                ? HttpStatusCode.InternalServerError
                : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}

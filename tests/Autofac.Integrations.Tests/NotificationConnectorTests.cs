using System.Net;
using System.Text;
using Autofac.Integrations;
using Microsoft.Extensions.Options;

namespace Autofac.Integrations.Tests;

public sealed class NotificationConnectorTests
{
    [Fact]
    public async Task SlackConnector_PostsWebhookPayload()
    {
        var requests = new List<HttpRequestMessage>();
        var connector = new SlackConnector(
            new HttpClient(new StubHttpMessageHandler(async request =>
            {
                requests.Add(await CloneAsync(request));
                return new HttpResponseMessage(HttpStatusCode.OK);
            })),
            Options.Create(new IntegrationOptions
            {
                Slack = new SlackOptions
                {
                    Enabled = true,
                    WebhookUrl = "https://hooks.slack.test/services/1"
                }
            }),
            new StubSecretStore(),
            new AllowAllPolicyEvaluationService(),
            new NoOpAuditRepository(),
            new NoOpWorkflowMetrics(),
            new StubCorrelationContext(),
            new NoOpWorkflowTracer());

        var result = await connector.SendNotificationAsync(new SendNotificationCommand("Run finished", "All green."));

        Assert.Equal("Slack notification sent: Run finished", result.Summary);
        Assert.Single(requests);
        Assert.Equal("https://hooks.slack.test/services/1", requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task TeamsConnector_PostsWebhookPayload()
    {
        var requests = new List<HttpRequestMessage>();
        var connector = new TeamsConnector(
            new HttpClient(new StubHttpMessageHandler(async request =>
            {
                requests.Add(await CloneAsync(request));
                return new HttpResponseMessage(HttpStatusCode.OK);
            })),
            Options.Create(new IntegrationOptions
            {
                Teams = new TeamsOptions
                {
                    Enabled = true,
                    WebhookUrl = "https://teams.test/webhook/1"
                }
            }),
            new StubSecretStore(),
            new AllowAllPolicyEvaluationService(),
            new NoOpAuditRepository(),
            new NoOpWorkflowMetrics(),
            new StubCorrelationContext(),
            new NoOpWorkflowTracer());

        var result = await connector.SendNotificationAsync(new SendNotificationCommand("Approval needed", "Please review."));

        Assert.Equal("Teams notification sent: Approval needed", result.Summary);
        Assert.Single(requests);
        Assert.Equal("https://teams.test/webhook/1", requests[0].RequestUri?.ToString());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Content is not null)
        {
            clone.Content = new StringContent(await request.Content.ReadAsStringAsync(), Encoding.UTF8, "application/json");
        }

        return clone;
    }
}

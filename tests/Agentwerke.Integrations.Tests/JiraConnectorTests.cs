using System.Net;
using System.Text;
using Agentwerke.Integrations;
using Microsoft.Extensions.Options;

namespace Agentwerke.Integrations.Tests;

public sealed class JiraConnectorTests
{
    [Fact]
    public async Task UpdateIssueStatusAsync_PostsTransitionAndComment()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await CloneAsync(request));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var connector = new JiraConnector(
            new HttpClient(handler) { BaseAddress = new Uri("https://jira.test/") },
            Options.Create(new IntegrationOptions
            {
                Jira = new JiraOptions
                {
                    Enabled = true,
                    ApiBaseUrl = "https://jira.test/",
                    Username = "robot@example.com",
                    ApiToken = "jira-token"
                }
            }),
            new StubSecretStore(),
            new AllowAllPolicyEvaluationService(),
            new NoOpAuditRepository(),
            new NoOpWorkflowMetrics(),
            new StubCorrelationContext(),
            new NoOpWorkflowTracer());

        var result = await connector.UpdateIssueStatusAsync(
            new UpdateJiraIssueStatusCommand("PROJ-123", "31", "Started by Autofac"),
            CancellationToken.None);

        Assert.True(result.TransitionApplied);
        Assert.True(result.CommentAdded);
        Assert.Equal("https://jira.test/browse/PROJ-123", result.IssueUrl);
        Assert.Equal(2, requests.Count);
        Assert.Equal("/rest/api/3/issue/PROJ-123/transitions", requests[0].RequestUri?.AbsolutePath);
        Assert.Equal("/rest/api/3/issue/PROJ-123/comment", requests[1].RequestUri?.AbsolutePath);
        Assert.Equal("Basic", requests[0].Headers.Authorization?.Scheme);

        var commentBody = await requests[1].Content!.ReadAsStringAsync();
        Assert.Contains("\"type\":\"doc\"", commentBody, StringComparison.Ordinal);
        Assert.Contains("Started by Autofac", commentBody, StringComparison.Ordinal);
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
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var content = await request.Content.ReadAsStringAsync();
            clone.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        return clone;
    }
}

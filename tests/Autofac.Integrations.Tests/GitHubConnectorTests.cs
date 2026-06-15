using System.Net;
using System.Text;
using Autofac.Integrations;
using Microsoft.Extensions.Options;

namespace Autofac.Integrations.Tests;

public sealed class GitHubConnectorTests
{
    [Fact]
    public async Task CreateBranchAsync_UsesConfiguredRepositoryAndDefaultBaseBranch()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await CloneAsync(request));

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/git/ref/heads/main")
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "ref": "refs/heads/main",
                      "object": { "sha": "abc123def456" }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/git/refs")
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "ref": "refs/heads/autofac/run-123",
                      "object": { "sha": "abc123def456" }
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });

        var connector = CreateConnector(handler);

        var result = await connector.CreateBranchAsync(
            new CreateGitHubBranchCommand(
                BranchName: "autofac/run-123",
                BaseBranch: null),
            CancellationToken.None);

        Assert.Equal("autofac/run-123", result.BranchName);
        Assert.Equal("main", result.BaseBranch);
        Assert.Equal("abc123def456", result.CommitSha);
        Assert.False(result.AlreadyExisted);
        Assert.Equal("https://github.com/octo/autofac/tree/autofac/run-123", result.BranchUrl);

        Assert.Equal(2, requests.Count);
        Assert.Equal("Bearer", requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("token-value", requests[0].Headers.Authorization?.Parameter);
        Assert.Contains("Autofac/1.0", requests[0].Headers.UserAgent.ToString(), StringComparison.Ordinal);

        var createBranchPayload = await requests[1].Content!.ReadAsStringAsync();
        Assert.Contains("\"ref\":\"refs/heads/autofac/run-123\"", createBranchPayload, StringComparison.Ordinal);
        Assert.Contains("\"sha\":\"abc123def456\"", createBranchPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePullRequestAsync_CommitsMarkerFileAndCreatesDraftPullRequest()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await CloneAsync(request));

            if (request.Method == HttpMethod.Put &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/contents/.autofac/runs/run-123/step-456-attempt-2.md")
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "content": {
                        "path": ".autofac/runs/run-123/step-456-attempt-2.md",
                        "html_url": "https://github.com/octo/autofac/blob/autofac/run-123/.autofac/runs/run-123/step-456-attempt-2.md"
                      },
                      "commit": {
                        "sha": "feedface1234"
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/pulls")
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/pulls")
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "number": 42,
                      "html_url": "https://github.com/octo/autofac/pull/42",
                      "state": "open"
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });

        var connector = CreateConnector(handler);

        var result = await connector.CreatePullRequestAsync(
            new CreateGitHubPullRequestCommand(
                RunId: "run-123",
                StepId: "step-456",
                Attempt: 2,
                HeadBranch: "autofac/run-123",
                BaseBranch: null,
                Title: "Autofac generated change",
                Body: "Generated from workflow execution.",
                CommitMessage: "Autofac marker commit"),
            CancellationToken.None);

        Assert.Equal(42, result.Number);
        Assert.Equal("https://github.com/octo/autofac/pull/42", result.PullRequestUrl);
        Assert.Equal("autofac/run-123", result.HeadBranch);
        Assert.Equal("main", result.BaseBranch);
        Assert.False(result.AlreadyExisted);
        Assert.Equal("feedface1234", result.CommitSha);
        Assert.Equal(".autofac/runs/run-123/step-456-attempt-2.md", result.MarkerPath);

        Assert.Equal(3, requests.Count);
        Assert.Equal("/repos/octo/autofac/contents/.autofac/runs/run-123/step-456-attempt-2.md", requests[0].RequestUri?.AbsolutePath);
        Assert.Equal("/repos/octo/autofac/pulls", requests[1].RequestUri?.AbsolutePath);
        Assert.Contains("head=octo%3Aautofac%2Frun-123", requests[1].RequestUri?.Query ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("base=main", requests[1].RequestUri?.Query ?? string.Empty, StringComparison.Ordinal);

        var commitPayload = await requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"message\":\"Autofac marker commit\"", commitPayload, StringComparison.Ordinal);
        Assert.Contains("\"branch\":\"autofac/run-123\"", commitPayload, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"R2VuZXJhdGVkIGZyb20gd29ya2Zsb3cgZXhlY3V0aW9uLg==\"", commitPayload, StringComparison.Ordinal);

        var pullPayload = await requests[2].Content!.ReadAsStringAsync();
        Assert.Contains("\"title\":\"Autofac generated change\"", pullPayload, StringComparison.Ordinal);
        Assert.Contains("\"head\":\"autofac/run-123\"", pullPayload, StringComparison.Ordinal);
        Assert.Contains("\"base\":\"main\"", pullPayload, StringComparison.Ordinal);
        Assert.Contains("\"draft\":true", pullPayload, StringComparison.Ordinal);
    }

    private static GitHubConnector CreateConnector(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.test/")
        };

        return new GitHubConnector(
            client,
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    Enabled = true,
                    ApiBaseUrl = "https://api.github.test/",
                    RepositoryOwner = "octo",
                    RepositoryName = "autofac",
                    PersonalAccessToken = "token-value",
                    DefaultBaseBranch = "main",
                    BranchPrefix = "autofac/run-",
                    CreateDraftPullRequests = true
                }
            }),
            new StubSecretStore(),
            new AllowAllPolicyEvaluationService(),
            new NoOpAuditRepository(),
            new NoOpWorkflowMetrics(),
            new StubCorrelationContext());
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
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
            clone.Content = new StringContent(content, Encoding.UTF8);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
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
}

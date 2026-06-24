using System.Net;
using System.Text;
using Autofac.Integrations;
using Microsoft.Extensions.Options;

namespace Autofac.Integrations.Tests;

public sealed class GitHubConnectorTests
{
    [Fact]
    public async Task GetIssueAsync_LoadsIssueDetailsAndComments()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await CloneAsync(request));

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/issues/135")
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "number": 135,
                      "title": "Implement PR collaboration ops",
                      "body": "Need reviewer and review support.",
                      "state": "open",
                      "html_url": "https://github.com/octo/autofac/issues/135",
                      "labels": [
                        { "name": "enhancement" },
                        { "name": "sdlc" }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/issues/135/comments")
            {
                return Json(HttpStatusCode.OK, """
                    [
                      {
                        "body": "Please include comments.",
                        "created_at": "2026-06-22T15:05:00Z",
                        "user": { "login": "reviewer-a" }
                      }
                    ]
                    """);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });

        var connector = CreateConnector(handler);

        var result = await connector.GetIssueAsync(135, CancellationToken.None);

        Assert.Equal(135, result.Number);
        Assert.Equal("Implement PR collaboration ops", result.Title);
        Assert.Equal("Need reviewer and review support.", result.Body);
        Assert.Equal(["enhancement", "sdlc"], result.Labels);
        Assert.Equal("open", result.State);
        Assert.Equal("https://github.com/octo/autofac/issues/135", result.IssueUrl);
        var comment = Assert.Single(result.Comments);
        Assert.Equal("reviewer-a", comment.Author);
        Assert.Equal("Please include comments.", comment.Body);

        Assert.Equal(2, requests.Count);
        Assert.Equal("/repos/octo/autofac/issues/135", requests[0].RequestUri?.AbsolutePath);
        Assert.Equal("/repos/octo/autofac/issues/135/comments", requests[1].RequestUri?.AbsolutePath);
    }

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

    [Fact]
    public async Task GetPullRequestAsync_ReturnsStateAndMergeDetails()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/pulls/42")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "number": 42,
                      "state": "closed",
                      "merged": true,
                      "merge_commit_sha": "feedface1234",
                      "head": { "ref": "autofac/run-123", "sha": "abc123" },
                      "base": { "ref": "main", "sha": "def456" }
                    }
                    """));
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });

        var connector = CreateConnector(handler);

        var result = await connector.GetPullRequestAsync(42, CancellationToken.None);

        Assert.Equal(42, result.Number);
        Assert.Equal("closed", result.State);
        Assert.True(result.Merged);
        Assert.Equal("feedface1234", result.MergeCommitSha);
        Assert.Equal("autofac/run-123", result.HeadBranch);
        Assert.Equal("abc123", result.HeadSha);
        Assert.Equal("main", result.BaseBranch);
    }

    [Fact]
    public async Task GetCheckStatusAsync_WhenAllRunsSucceeded_ReturnsCompletedSuccess()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/commits/abc123/check-runs")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "total_count": 2,
                      "check_runs": [
                        { "name": "build", "status": "completed", "conclusion": "success" },
                        { "name": "test", "status": "completed", "conclusion": "success" }
                      ]
                    }
                    """));
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });

        var connector = CreateConnector(handler);

        var result = await connector.GetCheckStatusAsync("abc123", CancellationToken.None);

        Assert.Equal("abc123", result.Ref);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal("completed", result.Status);
        Assert.Equal("success", result.Conclusion);
        Assert.Equal(2, result.CheckRuns.Count);
    }

    [Fact]
    public async Task GetCheckStatusAsync_WhenAnyRunFailed_ReturnsCompletedFailure()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, """
            {
              "total_count": 2,
              "check_runs": [
                { "name": "build", "status": "completed", "conclusion": "success" },
                { "name": "test", "status": "completed", "conclusion": "failure" }
              ]
            }
            """)));

        var connector = CreateConnector(handler);

        var result = await connector.GetCheckStatusAsync("abc123", CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("failure", result.Conclusion);
    }

    [Fact]
    public async Task GetCheckStatusAsync_WhenAnyRunStillInProgress_ReturnsInProgressWithNullConclusion()
    {
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, """
            {
              "total_count": 2,
              "check_runs": [
                { "name": "build", "status": "completed", "conclusion": "success" },
                { "name": "test", "status": "in_progress", "conclusion": null }
              ]
            }
            """)));

        var connector = CreateConnector(handler);

        var result = await connector.GetCheckStatusAsync("abc123", CancellationToken.None);

        Assert.Equal("in_progress", result.Status);
        Assert.Null(result.Conclusion);
    }

    [Fact]
    public async Task RequestReviewersAsync_PostsReviewerPayload()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await CloneAsync(request));

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/pulls/42/requested_reviewers")
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "html_url": "https://github.com/octo/autofac/pull/42",
                      "requested_reviewers": [
                        { "login": "alice" },
                        { "login": "bob" }
                      ]
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });

        var connector = CreateConnector(handler);

        var result = await connector.RequestReviewersAsync(
            new RequestGitHubReviewersCommand(42, ["alice", "bob"]),
            CancellationToken.None);

        Assert.Equal(42, result.PullNumber);
        Assert.Equal("https://github.com/octo/autofac/pull/42", result.PullRequestUrl);
        Assert.Equal(["alice", "bob"], result.RequestedReviewers);

        var payload = await requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"reviewers\":[\"alice\",\"bob\"]", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostReviewAsync_PostsCommentReview()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await CloneAsync(request));

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/pulls/42/reviews")
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "id": 9001,
                      "state": "COMMENTED",
                      "html_url": "https://github.com/octo/autofac/pull/42#pullrequestreview-9001"
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });

        var connector = CreateConnector(handler);

        var result = await connector.PostReviewAsync(
            new PostGitHubReviewCommand(42, "Looks good overall.", "COMMENT"),
            CancellationToken.None);

        Assert.Equal(9001, result.ReviewId);
        Assert.Equal(42, result.PullNumber);
        Assert.Equal("COMMENTED", result.State);
        Assert.Equal("COMMENT", result.Event);
        Assert.Equal("https://github.com/octo/autofac/pull/42#pullrequestreview-9001", result.ReviewUrl);

        var payload = await requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"body\":\"Looks good overall.\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"event\":\"COMMENT\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TriggerWorkflowDispatchAsync_PostsWorkflowDispatchPayloadWithExplicitInputs()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await CloneAsync(request));

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/repos/octo/autofac/actions/workflows/deploy-to-test.yml/dispatches")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });

        var connector = CreateConnector(handler);

        var result = await connector.TriggerWorkflowDispatchAsync(
            new TriggerGitHubWorkflowDispatchCommand(
                Ref: "abc123",
                Inputs: new Dictionary<string, string> { ["sha"] = "abc123" }),
            CancellationToken.None);

        Assert.Equal("deploy-to-test.yml", result.WorkflowFileName);
        Assert.Equal("abc123", result.Ref);

        var payload = await requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"ref\":\"abc123\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"sha\":\"abc123\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TriggerWorkflowDispatchAsync_WhenNoOverridesGiven_FallsBackToConfiguredDefaults()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await CloneAsync(request));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var connector = CreateConnector(handler);

        var result = await connector.TriggerWorkflowDispatchAsync(
            new TriggerGitHubWorkflowDispatchCommand(),
            CancellationToken.None);

        Assert.Equal("deploy-to-test.yml", result.WorkflowFileName);
        Assert.Equal("main", result.Ref);
        Assert.Equal("/repos/octo/autofac/actions/workflows/deploy-to-test.yml/dispatches", requests[0].RequestUri?.AbsolutePath);

        var payload = await requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("\"ref\":\"main\"", payload, StringComparison.Ordinal);
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
            new StubCorrelationContext(),
            new NoOpWorkflowTracer());
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

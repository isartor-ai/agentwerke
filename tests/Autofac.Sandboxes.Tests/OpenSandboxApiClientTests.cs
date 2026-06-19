using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Autofac.Sandboxes.Tests;

public sealed class OpenSandboxApiClientTests
{
    [Fact]
    public async Task CreateAsync_WaitsForSandboxAndCapturesLifecycleDiagnostics()
    {
        var handler = new SequencedHttpMessageHandler();
        handler.Enqueue(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://sandbox.local:8080/v1/sandboxes", request.RequestUri?.ToString());
            Assert.Equal("test-key", request.Headers.GetValues("OPEN-SANDBOX-API-KEY").Single());

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("python:3.12", body.RootElement.GetProperty("image").GetProperty("uri").GetString());
            Assert.Equal(120, body.RootElement.GetProperty("timeout").GetInt32());
            Assert.True(body.RootElement.GetProperty("secureAccess").GetBoolean());
            Assert.Equal("tail", body.RootElement.GetProperty("entrypoint")[0].GetString());
            Assert.Equal("deny", body.RootElement.GetProperty("networkPolicy").GetProperty("defaultAction").GetString());
            Assert.Equal("allow", body.RootElement.GetProperty("networkPolicy").GetProperty("egress")[0].GetProperty("action").GetString());
            Assert.Equal("api.github.com", body.RootElement.GetProperty("networkPolicy").GetProperty("egress")[0].GetProperty("target").GetString());

            return JsonResponse(HttpStatusCode.Accepted, """{"id":"sbx-1"}""", "create-req");
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://sandbox.local:8080/v1/sandboxes/sbx-1", request.RequestUri?.ToString());
            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """{"status":{"state":"Running"}}""",
                "inspect-req"));
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal(
                "http://sandbox.local:8080/v1/sandboxes/sbx-1/endpoints/44772?use_server_proxy=true",
                request.RequestUri?.ToString());

            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """{"endpoint":"proxy.local/sandboxes/sbx-1/port/44772","headers":{"X-EXECD-ACCESS-TOKEN":"execd-token"}}""",
                "endpoint-req"));
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://proxy.local/sandboxes/sbx-1/port/44772/ping", request.RequestUri?.ToString());
            Assert.Equal("execd-token", request.Headers.GetValues("X-EXECD-ACCESS-TOKEN").Single());
            return Task.FromResult(EmptyResponse(HttpStatusCode.OK, "ping-req"));
        });

        var client = CreateClient(handler, options: new OpenSandboxProviderOptions
        {
            ServerUrl = "http://sandbox.local:8080/v1",
            ApiKey = "test-key",
            UseServerProxy = true,
            ReadinessTimeoutSeconds = 5
        });

        var handle = await client.CreateAsync(
            new OpenSandboxCreateSandboxRequest(
                Image: "python:3.12",
                TimeoutSeconds: 120,
                ResourceLimits: new OpenSandboxResourceLimits(500, 512, 120, null),
                EnvironmentVariables: new Dictionary<string, string> { ["TRACE"] = "1" },
                Metadata: new Dictionary<string, string> { ["ticket"] = "126" },
                Volumes: [],
                NetworkPolicy: new OpenSandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, ["api.github.com"]),
                CredentialBindings: [],
                RequestedEndpoints: [new OpenSandboxResolveEndpointRequest(8080, "http", SecureAccess: true)],
                SecureAccess: true,
                WorkingDirectory: "/workspace",
                CommandExecutionMode: SandboxCommandExecutionMode.Foreground),
            CancellationToken.None);

        Assert.Equal("sbx-1", handle.SandboxId);
        Assert.NotNull(handle.Metadata);
        Assert.Equal("create-req", handle.Metadata!["lifecycle.create.request_id"]);
        Assert.Equal("inspect-req", handle.Metadata["lifecycle.inspect.request_id"]);
        Assert.Equal("endpoint-req", handle.Metadata["lifecycle.execd_endpoint.request_id"]);
        Assert.Equal("ping-req", handle.Metadata["execd.ping.request_id"]);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task RunCommandAsync_SessionMode_StreamsOutputAndDeletesSession()
    {
        var handler = new SequencedHttpMessageHandler();
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://sandbox.local:8080/v1/sandboxes/sbx-1/endpoints/44772", request.RequestUri?.ToString());
            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """{"endpoint":"execd.local","headers":{"X-EXECD-ACCESS-TOKEN":"execd-token"}}""",
                "endpoint-req"));
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://execd.local/ping", request.RequestUri?.ToString());
            return Task.FromResult(EmptyResponse(HttpStatusCode.OK, "ping-req"));
        });
        handler.Enqueue(async (request, cancellationToken) =>
        {
            Assert.Equal("http://execd.local/session", request.RequestUri?.ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("/workspace", body.RootElement.GetProperty("cwd").GetString());
            return JsonResponse(HttpStatusCode.OK, """{"session_id":"sess-1"}""", "session-req");
        });
        handler.Enqueue(async (request, cancellationToken) =>
        {
            Assert.Equal("http://execd.local/session/sess-1/run", request.RequestUri?.ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("/workspace", body.RootElement.GetProperty("cwd").GetString());
            Assert.Equal(30000, body.RootElement.GetProperty("timeout").GetInt64());
            Assert.Contains("'python' 'main.py'", body.RootElement.GetProperty("command").GetString(), StringComparison.Ordinal);

            const string sse = """
data: {"type":"init","text":"cmd-1","timestamp":1}

data: {"type":"stdout","text":"hello\n","timestamp":2}

data: {"type":"execution_complete","timestamp":3}

""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
                Headers = { { "X-Request-ID", "run-req" } }
            };
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("http://execd.local/session/sess-1", request.RequestUri?.ToString());
            return Task.FromResult(EmptyResponse(HttpStatusCode.OK, "delete-session-req"));
        });

        var client = CreateClient(handler);

        var result = await client.RunCommandAsync(
            "sbx-1",
            new OpenSandboxRunCommandRequest(
                Arguments: ["python", "main.py"],
                Mode: SandboxCommandExecutionMode.Session,
                WorkingDirectory: "/workspace",
                EnvironmentVariables: new Dictionary<string, string> { ["TRACE"] = "1" },
                TimeoutSeconds: 30,
                StandardInput: null,
                StreamOutput: true),
            CancellationToken.None);

        Assert.Equal(SandboxCommandState.Completed, result.State);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("sess-1", result.SessionId);
        Assert.Equal("cmd-1", result.ExecutionId);
        Assert.Equal("hello\n", result.Logs);
        Assert.Null(result.FailureReason);
        Assert.Single(result.StructuredLogs);
        Assert.Equal("stdout", result.StructuredLogs[0].Stream);
        Assert.NotNull(result.Diagnostics);
        Assert.Equal("run-req", result.Diagnostics!["execd.run.request_id"]);
        handler.AssertCompleted();
    }

    [Fact]
    public async Task CollectArtifactsAsync_DownloadsTextFilesAndSkipsMissingPaths()
    {
        var handler = new SequencedHttpMessageHandler();
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://sandbox.local:8080/v1/sandboxes/sbx-1/endpoints/44772", request.RequestUri?.ToString());
            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """{"endpoint":"execd.local","headers":{"X-EXECD-ACCESS-TOKEN":"execd-token"}}""",
                "endpoint-req"));
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://execd.local/ping", request.RequestUri?.ToString());
            return Task.FromResult(EmptyResponse(HttpStatusCode.OK, "ping-req"));
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://execd.local/files/info?path=%2Foutput", request.RequestUri?.ToString());
            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """{"/output":{"path":"/output","type":"directory"}}""",
                "files-info-req"));
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://execd.local/files/search?path=%2Foutput&pattern=%2A%2A", request.RequestUri?.ToString());
            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """[{"path":"/output/result.json","type":"file"},{"path":"/output/sub","type":"directory"},{"path":"/output/sub/report.txt","type":"file"}]""",
                "files-search-req"));
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://execd.local/files/download?path=%2Foutput%2Fresult.json", request.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                Headers = { { "X-Request-ID", "download-1" } }
            });
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://execd.local/files/download?path=%2Foutput%2Fsub%2Freport.txt", request.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok", Encoding.UTF8, "text/plain"),
                Headers = { { "X-Request-ID", "download-2" } }
            });
        });
        handler.Enqueue((request, _) =>
        {
            Assert.Equal("http://execd.local/files/info?path=%2Fmissing", request.RequestUri?.ToString());
            return Task.FromResult(JsonResponse(
                HttpStatusCode.NotFound,
                """{"code":"NOT_FOUND","message":"missing"}""",
                "missing-req"));
        });

        var client = CreateClient(handler);

        var artifacts = await client.CollectArtifactsAsync(
            "sbx-1",
            new OpenSandboxCollectArtifactsRequest(["/output", "/missing"]),
            CancellationToken.None);

        Assert.Collection(
            artifacts.OrderBy(static artifact => artifact.Path, StringComparer.OrdinalIgnoreCase),
            artifact =>
            {
                Assert.Equal("result.json", artifact.Path);
                Assert.Equal("{}", artifact.Content);
            },
            artifact =>
            {
                Assert.Equal("sub/report.txt", artifact.Path);
                Assert.Equal("ok", artifact.Content);
            });

        handler.AssertCompleted();
    }

    private static OpenSandboxApiClient CreateClient(
        SequencedHttpMessageHandler handler,
        OpenSandboxProviderOptions? options = null)
    {
        var sandboxOptions = new SandboxOptions
        {
            OpenSandbox = options ?? new OpenSandboxProviderOptions
            {
                ServerUrl = "http://sandbox.local:8080/v1",
                ApiKey = "test-key",
                ReadinessTimeoutSeconds = 5
            }
        };

        return new OpenSandboxApiClient(
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            Options.Create(sandboxOptions));
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json,
        string? requestId = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            response.Headers.Add("X-Request-ID", requestId);
        }

        return response;
    }

    private static HttpResponseMessage EmptyResponse(
        HttpStatusCode statusCode,
        string? requestId = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            response.Headers.Add("X-Request-ID", requestId);
        }

        return response;
    }

    private sealed class SequencedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps = new();

        public void Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step) =>
            _steps.Enqueue(step);

        public void AssertCompleted() => Assert.Empty(_steps);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_steps.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
            }

            return _steps.Dequeue()(request, cancellationToken);
        }
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Autofac.Agents.Models;
using Microsoft.Extensions.Options;

namespace Autofac.Agents.Tests;

public sealed class OpenAiCompatibleLanguageModelClientTests
{
    private static LanguageModelToolDefinition[] Tools() =>
    [
        new LanguageModelToolDefinition(
            "github.post_review",
            "Post a review",
            [
                new LanguageModelToolParameter("decision", "string", "Outcome", Required: true,
                    EnumValues: ["approve", "request_changes"]),
                new LanguageModelToolParameter("labels", "array", "Labels", ItemType: "string"),
            ]),
    ];

    private static OpenAiCompatibleLanguageModelClient Client(QueuedHttpServer server) =>
        new(new HttpClient(), Options.Create(new LanguageModelOptions
        {
            ApiKey = "test-key",
            ApiBaseUrl = server.BaseUrl,
            Model = "gpt-4o",
            MaxTokens = 128,
            MaxToolIterations = 5,
        }));

    [Fact]
    public async Task RunAsync_EmitsOpenAiToolSchemaAndMessages()
    {
        const string finalResponse = """
            {"model":"gpt-4o","choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"done"}}],"usage":{"prompt_tokens":5,"completion_tokens":2}}
            """;
        using var server = QueuedHttpServer.Start(finalResponse);

        var response = await Client(server).RunAsync(
            new LanguageModelRequest("system", "review the PR", Tools(), MaxTokens: 128),
            (_, _) => throw new InvalidOperationException("No tool calls expected."),
            CancellationToken.None);

        Assert.True(response.Succeeded, response.FailureReason);
        Assert.Equal("done", response.Output);
        Assert.Equal(5, response.Usage.InputTokens);
        Assert.Equal(2, response.Usage.OutputTokens);

        using var doc = JsonDocument.Parse(server.ReceivedBodies[0]);
        var root = doc.RootElement;
        Assert.Equal("gpt-4o", root.GetProperty("model").GetString());
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());

        var function = root.GetProperty("tools")[0].GetProperty("function");
        Assert.Equal("github_post_review", function.GetProperty("name").GetString());
        var properties = function.GetProperty("parameters").GetProperty("properties");
        var enumValues = properties.GetProperty("decision").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["approve", "request_changes"], enumValues);
        Assert.Equal("array", properties.GetProperty("labels").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("labels").GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public async Task RunAsync_ExecutesToolCallLoop_AndAppendsToolResult()
    {
        const string toolCallResponse = """
            {"model":"gpt-4o","choices":[{"finish_reason":"tool_calls","message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"github_post_review","arguments":"{\"decision\":\"approve\"}"}}]}}],"usage":{"prompt_tokens":10,"completion_tokens":3}}
            """;
        const string finalResponse = """
            {"model":"gpt-4o","choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"all set"}}],"usage":{"prompt_tokens":5,"completion_tokens":2}}
            """;
        using var server = QueuedHttpServer.Start(toolCallResponse, finalResponse);

        var executed = new List<LanguageModelToolCall>();
        var response = await Client(server).RunAsync(
            new LanguageModelRequest("system", "review the PR", Tools(), MaxTokens: 128),
            (call, _) =>
            {
                executed.Add(call);
                return Task.FromResult(new LanguageModelToolResult(call.Id, "posted"));
            },
            CancellationToken.None);

        Assert.True(response.Succeeded, response.FailureReason);
        Assert.Equal("all set", response.Output);
        Assert.Equal(15, response.Usage.InputTokens);
        Assert.Equal(5, response.Usage.OutputTokens);

        var call = Assert.Single(executed);
        Assert.Equal("github.post_review", call.Name); // de-sanitized back to the dotted name
        Assert.Equal("approve", call.Input["decision"]);

        // The second request must carry the assistant tool_calls turn and the tool result.
        Assert.Equal(2, server.ReceivedBodies.Count);
        using var doc = JsonDocument.Parse(server.ReceivedBodies[1]);
        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Contains(messages, m => m.GetProperty("role").GetString() == "assistant"
            && m.TryGetProperty("tool_calls", out _));
        var toolMessage = Assert.Single(messages, m => m.GetProperty("role").GetString() == "tool");
        Assert.Equal("call_1", toolMessage.GetProperty("tool_call_id").GetString());
        Assert.Equal("posted", toolMessage.GetProperty("content").GetString());
    }

    private sealed class QueuedHttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Queue<string> _responses;
        private readonly List<string> _bodies = [];
        private readonly object _gate = new();

        private QueuedHttpServer(HttpListener listener, string baseUrl, IEnumerable<string> responses)
        {
            _listener = listener;
            BaseUrl = baseUrl;
            _responses = new Queue<string>(responses);
        }

        public string BaseUrl { get; }

        public IReadOnlyList<string> ReceivedBodies
        {
            get { lock (_gate) { return _bodies.ToArray(); } }
        }

        public static QueuedHttpServer Start(params string[] responses)
        {
            var port = FreeTcpPort();
            var baseUrl = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(baseUrl);
            listener.Start();
            var server = new QueuedHttpServer(listener, baseUrl, responses);
            _ = server.AcceptLoopAsync();
            return server;
        }

        private async Task AcceptLoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var body = await reader.ReadToEndAsync();
                    lock (_gate)
                    {
                        _bodies.Add(body);
                    }
                }

                string payload;
                lock (_gate)
                {
                    payload = _responses.Count > 0 ? _responses.Dequeue() : "{}";
                }

                var bytes = Encoding.UTF8.GetBytes(payload);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.OutputStream.Close();
            }
        }

        private static int FreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose() => _listener.Close();
    }
}

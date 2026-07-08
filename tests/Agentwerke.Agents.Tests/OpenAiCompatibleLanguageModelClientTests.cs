using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Agentwerke.Agents.Models;
using Agentwerke.Workflows.Runtime;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents.Tests;

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

    private static OpenAiCompatibleLanguageModelClient Client(
        QueuedHttpServer server,
        HttpClient? httpClient = null,
        int timeoutSeconds = 100) =>
        new(httpClient ?? new HttpClient(), Options.Create(new LanguageModelOptions
        {
            ApiKey = "test-key",
            ApiBaseUrl = server.BaseUrl,
            Model = "gpt-4o",
            MaxTokens = 128,
            MaxToolIterations = 5,
            TimeoutSeconds = timeoutSeconds,
        }));

    /// <summary>Builds an SSE chat-completions stream from JSON chunk bodies, terminated by [DONE].</summary>
    private static string Sse(params string[] chunks) =>
        string.Concat(chunks.Select(c => $"data: {c}\n\n")) + "data: [DONE]\n\n";

    private const string UsageChunk =
        """{"model":"gpt-4o","choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":2}}""";

    [Fact]
    public async Task RunAsync_EmitsOpenAiToolSchemaAndStreamsMessages()
    {
        using var server = QueuedHttpServer.Start(Sse(
            """{"model":"gpt-4o","choices":[{"delta":{"content":"done"}}]}""",
            UsageChunk));

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
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.True(root.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
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
    public async Task RunAsync_ExtractsAgentReasoningBlockFromFinalOutput()
    {
        using var server = QueuedHttpServer.Start(Sse(
            """{"model":"gpt-4o","choices":[{"delta":{"content":"<agent_reasoning>Checked the issue, inspected available tools, and chose a minimal patch.</agent_reasoning>\nFinal answer only."}}]}""",
            UsageChunk));

        var response = await Client(server).RunAsync(
            new LanguageModelRequest("system", "review the PR", Tools(), MaxTokens: 128),
            (_, _) => throw new InvalidOperationException("No tool calls expected."),
            CancellationToken.None);

        Assert.True(response.Succeeded, response.FailureReason);
        Assert.Equal("Final answer only.", response.Output);
        Assert.Equal(
            "Checked the issue, inspected available tools, and chose a minimal patch.",
            response.ReasoningSummary);
    }

    [Fact]
    public async Task RunAsync_StreamsNativeReasoningContentCumulatively()
    {
        // A reasoning model (DeepSeek R1, GLM, …) emits its chain of thought in reasoning_content
        // deltas before the answer. Each progress update carries the reasoning so far.
        using var server = QueuedHttpServer.Start(Sse(
            """{"model":"deepseek-r1","choices":[{"delta":{"reasoning_content":"First, I read the issue. "}}]}""",
            """{"model":"deepseek-r1","choices":[{"delta":{"reasoning_content":"Then I plan a minimal change.\n"}}]}""",
            """{"model":"deepseek-r1","choices":[{"delta":{"content":"Here is the plan."}}]}""",
            """{"model":"deepseek-r1","choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":8,"completion_tokens":6}}"""));
        var updates = new List<AgentExecutionProgressUpdate>();

        var response = await Client(server).RunAsync(
            new LanguageModelRequest("system", "review the PR", Tools(), MaxTokens: 128),
            (_, _) => throw new InvalidOperationException("No tool calls expected."),
            CancellationToken.None,
            (update, _) => { updates.Add(update); return Task.CompletedTask; });

        Assert.True(response.Succeeded, response.FailureReason);
        Assert.Equal("Here is the plan.", response.Output);
        Assert.Equal("First, I read the issue. Then I plan a minimal change.", response.ReasoningSummary);

        Assert.NotEmpty(updates);
        Assert.All(updates, u => Assert.Equal(AgentExecutionProgressKinds.Reasoning, u.Kind));
        // Cumulative growth: each emitted summary extends the previous one (prefix-extension the UI collapses).
        for (var i = 1; i < updates.Count; i++)
        {
            Assert.StartsWith(updates[i - 1].Summary!.Trim(), updates[i].Summary!.Trim());
        }
        Assert.Equal("First, I read the issue. Then I plan a minimal change.", updates[^1].Summary);
    }

    [Fact]
    public async Task RunAsync_ExtractsThinkBlockReasoning()
    {
        // Models that inline reasoning in <think>…</think> in the content channel.
        using var server = QueuedHttpServer.Start(Sse(
            """{"model":"qwen","choices":[{"delta":{"content":"<think>Consider the tradeoffs"}}]}""",
            """{"model":"qwen","choices":[{"delta":{"content":" carefully.</think>The answer is 42."}}]}""",
            UsageChunk));
        var updates = new List<AgentExecutionProgressUpdate>();

        var response = await Client(server).RunAsync(
            new LanguageModelRequest("system", "review the PR", Tools(), MaxTokens: 128),
            (_, _) => throw new InvalidOperationException("No tool calls expected."),
            CancellationToken.None,
            (update, _) => { updates.Add(update); return Task.CompletedTask; });

        Assert.True(response.Succeeded, response.FailureReason);
        Assert.Equal("The answer is 42.", response.Output);
        Assert.Equal("Consider the tradeoffs carefully.", response.ReasoningSummary);
        Assert.Contains(updates, u => u.Summary == "Consider the tradeoffs carefully.");
    }

    [Fact]
    public async Task RunAsync_ExecutesStreamedToolCallLoop_AndAppendsToolResult()
    {
        // Tool call arguments arrive fragmented across deltas and must be concatenated by index.
        using var server = QueuedHttpServer.Start(
            Sse(
                """{"model":"gpt-4o","choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"github_post_review","arguments":""}}]}}]}""",
                """{"model":"gpt-4o","choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"decision\":"}}]}}]}""",
                """{"model":"gpt-4o","choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"approve\"}"}}]}}]}""",
                """{"model":"gpt-4o","choices":[{"delta":{},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":10,"completion_tokens":3}}"""),
            Sse(
                """{"model":"gpt-4o","choices":[{"delta":{"content":"all set"}}]}""",
                UsageChunk));

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

    [Fact]
    public async Task RunAsync_WhenAssistantCommentsBeforeToolCall_EmitsVisibleReasoningProgress()
    {
        using var server = QueuedHttpServer.Start(
            Sse(
                """{"model":"gpt-4o","choices":[{"delta":{"content":"<agent_reasoning>Loading repo context and preparing the review comment.</agent_reasoning>"}}]}""",
                """{"model":"gpt-4o","choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"github_post_review","arguments":"{\"decision\":\"approve\"}"}}]}}]}""",
                """{"model":"gpt-4o","choices":[{"delta":{},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":10,"completion_tokens":3}}"""),
            Sse(
                """{"model":"gpt-4o","choices":[{"delta":{"content":"all set"}}]}""",
                UsageChunk));
        var updates = new List<AgentExecutionProgressUpdate>();

        var response = await Client(server).RunAsync(
            new LanguageModelRequest("system", "review the PR", Tools(), MaxTokens: 128),
            (call, _) => Task.FromResult(new LanguageModelToolResult(call.Id, "posted")),
            CancellationToken.None,
            (update, _) =>
            {
                updates.Add(update);
                return Task.CompletedTask;
            });

        Assert.True(response.Succeeded, response.FailureReason);
        var reasoningUpdates = updates.Where(u => u.Kind == AgentExecutionProgressKinds.Reasoning).ToArray();
        Assert.Contains(reasoningUpdates, u => u.Summary == "Loading repo context and preparing the review comment.");
    }

    [Fact]
    public async Task RunAsync_WhenRequestTimesOut_ReturnsFailureInsteadOfThrowing()
    {
        using var server = QueuedHttpServer.Start(responseDelay: TimeSpan.FromSeconds(2), "{}");
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(1)
        };

        var response = await Client(server, httpClient, timeoutSeconds: 1).RunAsync(
            new LanguageModelRequest("system", "review the PR", Tools(), MaxTokens: 128),
            (_, _) => throw new InvalidOperationException("No tool calls expected."),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Null(response.Output);
        Assert.Contains("timed out after 1s", response.FailureReason, StringComparison.Ordinal);
    }

    private sealed class QueuedHttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Queue<string> _responses;
        private readonly List<string> _bodies = [];
        private readonly object _gate = new();
        private readonly TimeSpan _responseDelay;

        private QueuedHttpServer(
            HttpListener listener,
            string baseUrl,
            IEnumerable<string> responses,
            TimeSpan responseDelay)
        {
            _listener = listener;
            BaseUrl = baseUrl;
            _responses = new Queue<string>(responses);
            _responseDelay = responseDelay;
        }

        public string BaseUrl { get; }

        public IReadOnlyList<string> ReceivedBodies
        {
            get { lock (_gate) { return _bodies.ToArray(); } }
        }

        public static QueuedHttpServer Start(params string[] responses) =>
            Start(responseDelay: TimeSpan.Zero, responses);

        public static QueuedHttpServer Start(TimeSpan responseDelay, params string[] responses)
        {
            var port = FreeTcpPort();
            var baseUrl = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(baseUrl);
            listener.Start();
            var server = new QueuedHttpServer(listener, baseUrl, responses, responseDelay);
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

                if (_responseDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_responseDelay);
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

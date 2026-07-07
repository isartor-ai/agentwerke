using System.Net;
using System.Text;
using System.Text.Json;
using Agentwerke.Agents.Models;
using Agentwerke.Workflows.Runtime;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents.Tests;

public sealed class AnthropicLanguageModelClientTests
{
    [Fact]
    public async Task RunAsync_WithCustomApiBaseUrl_SendsStreamingRequestToConfiguredHostNotRealAnthropic()
    {
        using var server = FakeAnthropicServer.Start(
            BuildTextStreamResponse(["stubbed response"], inputTokens: 11, outputTokens: 7));

        using var httpClient = new HttpClient();
        var client = new AnthropicLanguageModelClient(httpClient, Options.Create(new LanguageModelOptions
        {
            ApiKey = "test-key",
            ApiBaseUrl = server.BaseUrl,
            Model = "claude-sonnet-4-6",
            MaxTokens = 256
        }));

        var response = await client.RunAsync(
            new LanguageModelRequest(
                SystemPrompt: "system",
                UserPrompt: "hello",
                Tools: [],
                MaxTokens: 256),
            (_, _) => throw new InvalidOperationException("No tool calls expected."),
            CancellationToken.None);

        Assert.True(response.Succeeded, response.FailureReason);
        Assert.Equal("stubbed response", response.Output);
        Assert.Equal(11, response.Usage.InputTokens);
        Assert.Equal(7, response.Usage.OutputTokens);

        var receivedUri = await server.GetReceivedRequestUriAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(server.BaseUri.Host, receivedUri.Host);
        Assert.Equal(server.BaseUri.Port, receivedUri.Port);

        using var bodyDocument = JsonDocument.Parse(Assert.Single(server.ReceivedBodies));
        Assert.True(bodyDocument.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_WhenResponseStreamsVisibleReasoning_EmitsCumulativeReasoningProgress()
    {
        using var server = FakeAnthropicServer.Start(
            BuildTextStreamResponse(
                [
                    "<agent_reasoning>Inspecting ",
                    "repo and ",
                    "tests.</agent_reasoning>\nDone."
                ],
                inputTokens: 19,
                outputTokens: 9));

        using var httpClient = new HttpClient();
        var client = new AnthropicLanguageModelClient(httpClient, Options.Create(new LanguageModelOptions
        {
            ApiKey = "test-key",
            ApiBaseUrl = server.BaseUrl,
            Model = "claude-sonnet-4-6",
            MaxTokens = 256
        }));

        var updates = new List<AgentExecutionProgressUpdate>();
        var response = await client.RunAsync(
            new LanguageModelRequest(
                SystemPrompt: "system",
                UserPrompt: "hello",
                Tools: [],
                MaxTokens: 256),
            (_, _) => throw new InvalidOperationException("No tool calls expected."),
            CancellationToken.None,
            (update, _) =>
            {
                updates.Add(update);
                return Task.CompletedTask;
            });

        Assert.True(response.Succeeded, response.FailureReason);
        Assert.Equal("Done.", response.Output);
        Assert.Equal("Inspecting repo and tests.", response.ReasoningSummary);
        Assert.Collection(
            updates,
            update =>
            {
                Assert.Equal(AgentExecutionProgressKinds.Reasoning, update.Kind);
                Assert.Equal("Inspecting", update.Summary);
            },
            update =>
            {
                Assert.Equal(AgentExecutionProgressKinds.Reasoning, update.Kind);
                Assert.Equal("Inspecting repo and", update.Summary);
            },
            update =>
            {
                Assert.Equal(AgentExecutionProgressKinds.Reasoning, update.Kind);
                Assert.Equal("Inspecting repo and tests.", update.Summary);
            });
    }

    private static string BuildTextStreamResponse(
        IReadOnlyList<string> textDeltas,
        int inputTokens,
        int outputTokens,
        string model = "claude-sonnet-4-6",
        string stopReason = "end_turn")
    {
        var builder = new StringBuilder();
        AppendSseEvent(builder, "message_start", new
        {
            type = "message_start",
            message = new
            {
                id = "msg_test",
                type = "message",
                role = "assistant",
                model,
                content = Array.Empty<object>(),
                stop_reason = (string?)null,
                stop_sequence = (string?)null,
                usage = new
                {
                    input_tokens = inputTokens,
                    output_tokens = 0,
                    cache_creation_input_tokens = 0,
                    cache_read_input_tokens = 0
                }
            }
        });
        AppendSseEvent(builder, "content_block_start", new
        {
            type = "content_block_start",
            index = 0,
            content_block = new
            {
                type = "text",
                text = string.Empty
            }
        });

        foreach (var delta in textDeltas)
        {
            AppendSseEvent(builder, "content_block_delta", new
            {
                type = "content_block_delta",
                index = 0,
                delta = new
                {
                    type = "text_delta",
                    text = delta
                }
            });
        }

        AppendSseEvent(builder, "content_block_stop", new
        {
            type = "content_block_stop",
            index = 0
        });
        AppendSseEvent(builder, "message_delta", new
        {
            type = "message_delta",
            delta = new
            {
                stop_reason = stopReason,
                stop_sequence = (string?)null
            },
            usage = new
            {
                output_tokens = outputTokens
            }
        });
        AppendSseEvent(builder, "message_stop", new
        {
            type = "message_stop"
        });

        return builder.ToString();
    }

    private static void AppendSseEvent(StringBuilder builder, string eventName, object payload)
    {
        builder.Append("event: ").Append(eventName).Append('\n');
        builder.Append("data: ").Append(JsonSerializer.Serialize(payload)).Append("\n\n");
    }

    private sealed class FakeAnthropicServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Queue<string> _responses;
        private readonly List<string> _receivedBodies = [];
        private readonly object _gate = new();
        private readonly TaskCompletionSource<Uri> _receivedRequestUri = new();

        private FakeAnthropicServer(HttpListener listener, string baseUrl, IEnumerable<string> responses)
        {
            _listener = listener;
            BaseUrl = baseUrl;
            BaseUri = new Uri(baseUrl);
            _responses = new Queue<string>(responses);
        }

        public string BaseUrl { get; }

        public Uri BaseUri { get; }

        public IReadOnlyList<string> ReceivedBodies
        {
            get
            {
                lock (_gate)
                {
                    return _receivedBodies.ToArray();
                }
            }
        }

        public static FakeAnthropicServer Start(params string[] responses)
        {
            var port = GetFreeTcpPort();
            var baseUrl = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(baseUrl);
            listener.Start();

            var server = new FakeAnthropicServer(listener, baseUrl, responses);
            _ = server.AcceptLoopAsync();
            return server;
        }

        public Task<Uri> GetReceivedRequestUriAsync(TimeSpan timeout) =>
            _receivedRequestUri.Task.WaitAsync(timeout);

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

                _receivedRequestUri.TrySetResult(context.Request.Url!);

                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var body = await reader.ReadToEndAsync();
                    lock (_gate)
                    {
                        _receivedBodies.Add(body);
                    }
                }

                string payload;
                lock (_gate)
                {
                    payload = _responses.Count > 0
                        ? _responses.Dequeue()
                        : BuildTextStreamResponse([""], inputTokens: 0, outputTokens: 0);
                }

                var bytes = Encoding.UTF8.GetBytes(payload);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/event-stream";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.OutputStream.Close();
            }
        }

        private static int GetFreeTcpPort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose() => _listener.Close();
    }
}

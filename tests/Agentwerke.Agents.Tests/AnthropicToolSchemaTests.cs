using System.Net;
using System.Text;
using System.Text.Json;
using Agentwerke.Agents.Models;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents.Tests;

public sealed class AnthropicToolSchemaTests
{
    [Fact]
    public async Task RunAsync_EmitsEnumAndArrayItems_InToolInputSchema()
    {
        using var server = await BodyCapturingServer.StartAsync();
        using var httpClient = new HttpClient();
        var client = new AnthropicLanguageModelClient(httpClient, Options.Create(new LanguageModelOptions
        {
            ApiKey = "test-key",
            ApiBaseUrl = server.BaseUrl,
            Model = "claude-sonnet-4-6",
            MaxTokens = 128
        }));

        var tools = new[]
        {
            new LanguageModelToolDefinition(
                Name: "github.post_review",
                Description: "Post a review",
                Parameters: new[]
                {
                    new LanguageModelToolParameter(
                        Name: "decision",
                        Type: "string",
                        Description: "Review outcome",
                        Required: true,
                        EnumValues: new[] { "approve", "request_changes", "comment" }),
                    new LanguageModelToolParameter(
                        Name: "labels",
                        Type: "array",
                        Description: "Labels to apply",
                        ItemType: "string")
                })
        };

        var response = await client.RunAsync(
            new LanguageModelRequest("system", "review the PR", tools, MaxTokens: 128),
            (_, _) => throw new InvalidOperationException("No tool calls expected."),
            CancellationToken.None);

        Assert.True(response.Succeeded, response.FailureReason);

        var body = await server.GetReceivedBodyAsync(TimeSpan.FromSeconds(5));
        using var doc = JsonDocument.Parse(body);

        var schema = doc.RootElement
            .GetProperty("tools")[0]
            .GetProperty("input_schema");
        var properties = schema.GetProperty("properties");

        var decision = properties.GetProperty("decision");
        var enumValues = decision.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "approve", "request_changes", "comment" }, enumValues);

        var labels = properties.GetProperty("labels");
        Assert.Equal("array", labels.GetProperty("type").GetString());
        Assert.Equal("string", labels.GetProperty("items").GetProperty("type").GetString());
    }

    private sealed class BodyCapturingServer : IDisposable
    {
        private const string ResponseBody = """
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_test","type":"message","role":"assistant","model":"claude-sonnet-4-6","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":5,"output_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"done"}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":2}}

            event: message_stop
            data: {"type":"message_stop"}
            """;

        private readonly HttpListener _listener;
        private readonly TaskCompletionSource<string> _body = new();

        private BodyCapturingServer(HttpListener listener, string baseUrl)
        {
            _listener = listener;
            BaseUrl = baseUrl;
        }

        public string BaseUrl { get; }

        public static Task<BodyCapturingServer> StartAsync()
        {
            var port = GetFreeTcpPort();
            var baseUrl = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(baseUrl);
            listener.Start();

            var server = new BodyCapturingServer(listener, baseUrl);
            _ = server.AcceptOneRequestAsync();
            return Task.FromResult(server);
        }

        public Task<string> GetReceivedBodyAsync(TimeSpan timeout) => _body.Task.WaitAsync(timeout);

        private async Task AcceptOneRequestAsync()
        {
            var context = await _listener.GetContextAsync();
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                _body.TrySetResult(await reader.ReadToEndAsync());
            }

            var bytes = Encoding.UTF8.GetBytes(ResponseBody);
            context.Response.ContentType = "text/event-stream";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.OutputStream.Close();
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

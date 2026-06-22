using System.Net;
using System.Text;
using Autofac.Agents.Models;
using Microsoft.Extensions.Options;

namespace Autofac.Agents.Tests;

public sealed class AnthropicLanguageModelClientTests
{
    [Fact]
    public async Task RunAsync_WithCustomApiBaseUrl_SendsRequestToConfiguredHostNotRealAnthropic()
    {
        // AnthropicClient (the SDK) builds request URLs from its own ApiUrlFormat
        // property, not from HttpClient.BaseAddress. A prior implementation only set
        // BaseAddress, which the SDK silently ignores — requests went to the real
        // api.anthropic.com regardless of configuration. This test fails against that
        // implementation (the listener never receives a request) and only passes once
        // AnthropicLanguageModelClient actually overrides ApiUrlFormat.
        using var server = await FakeAnthropicServer.StartAsync();

        var client = new AnthropicLanguageModelClient(Options.Create(new LanguageModelOptions
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
    }

    private sealed class FakeAnthropicServer : IDisposable
    {
        private const string ResponseBody = """
            {
              "id": "msg_test",
              "type": "message",
              "role": "assistant",
              "model": "claude-sonnet-4-6",
              "content": [{ "type": "text", "text": "stubbed response" }],
              "stop_reason": "end_turn",
              "stop_sequence": null,
              "usage": { "input_tokens": 11, "output_tokens": 7 }
            }
            """;

        private readonly HttpListener _listener;
        private readonly TaskCompletionSource<Uri> _receivedRequestUri = new();

        private FakeAnthropicServer(HttpListener listener, string baseUrl)
        {
            _listener = listener;
            BaseUrl = baseUrl;
            BaseUri = new Uri(baseUrl);
        }

        public string BaseUrl { get; }

        public Uri BaseUri { get; }

        public static Task<FakeAnthropicServer> StartAsync()
        {
            var port = GetFreeTcpPort();
            var baseUrl = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(baseUrl);
            listener.Start();

            var server = new FakeAnthropicServer(listener, baseUrl);
            _ = server.AcceptOneRequestAsync();
            return Task.FromResult(server);
        }

        public Task<Uri> GetReceivedRequestUriAsync(TimeSpan timeout) =>
            _receivedRequestUri.Task.WaitAsync(timeout);

        private async Task AcceptOneRequestAsync()
        {
            var context = await _listener.GetContextAsync();
            _receivedRequestUri.TrySetResult(context.Request.Url!);

            var bytes = Encoding.UTF8.GetBytes(ResponseBody);
            context.Response.ContentType = "application/json";
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

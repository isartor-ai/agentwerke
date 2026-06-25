using System.Net;
using Autofac.Agents.Models;

namespace Autofac.Agents.Tests;

public sealed class AnthropicRetryHandlerTests
{
    private static AnthropicRetryHandler CreateHandler(LanguageModelOptions options, HttpMessageHandler inner)
    {
        // No-op delay so backoff logic is exercised without real waiting.
        var handler = new AnthropicRetryHandler(options, static (_, _) => Task.CompletedTask)
        {
            InnerHandler = inner
        };
        return handler;
    }

    private static async Task<HttpResponseMessage> SendAsync(AnthropicRetryHandler handler)
    {
        using var client = new HttpClient(handler);
        return await client.PostAsync("http://localhost/v1/messages", new StringContent("{\"x\":1}"));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(529)] // Anthropic overloaded_error
    [InlineData(500)]
    [InlineData(503)]
    public async Task RunAsync_TransientStatus_IsRetriedThenSucceeds(int transientStatus)
    {
        var stub = new SequencedHandler((HttpStatusCode)transientStatus, HttpStatusCode.OK);
        using var handler = CreateHandler(new LanguageModelOptions { MaxRetries = 3, RetryBaseDelayMs = 1 }, stub);

        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.CallCount);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task RunAsync_NonTransient4xx_IsNotRetried(int terminalStatus)
    {
        var stub = new SequencedHandler((HttpStatusCode)terminalStatus, HttpStatusCode.OK);
        using var handler = CreateHandler(new LanguageModelOptions { MaxRetries = 3, RetryBaseDelayMs = 1 }, stub);

        using var response = await SendAsync(handler);

        Assert.Equal((HttpStatusCode)terminalStatus, response.StatusCode);
        Assert.Equal(1, stub.CallCount);
    }

    [Fact]
    public async Task RunAsync_ExhaustsRetries_ReturnsLastTransientResponse()
    {
        var stub = new SequencedHandler(
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests);
        using var handler = CreateHandler(new LanguageModelOptions { MaxRetries = 2, RetryBaseDelayMs = 1 }, stub);

        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, stub.CallCount); // initial attempt + 2 retries
    }

    [Fact]
    public async Task RunAsync_MaxRetriesZero_DoesNotRetry()
    {
        var stub = new SequencedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var handler = CreateHandler(new LanguageModelOptions { MaxRetries = 0 }, stub);

        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, stub.CallCount);
    }

    [Fact]
    public async Task RunAsync_RetriedRequest_ReplaysBufferedBody()
    {
        var stub = new SequencedHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        using var handler = CreateHandler(new LanguageModelOptions { MaxRetries = 1, RetryBaseDelayMs = 1 }, stub);

        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.CallCount);
        // Both the initial and the replayed request must carry the same body.
        Assert.Equal(2, stub.ObservedBodies.Count);
        Assert.All(stub.ObservedBodies, body => Assert.Equal("{\"x\":1}", body));
    }

    [Fact]
    public async Task RunAsync_RespectsRetryAfterDelayHeader()
    {
        TimeSpan? observedDelay = null;
        var stub = new SequencedHandler(
            (HttpStatusCode)429,
            HttpStatusCode.OK)
        {
            RetryAfterSeconds = 7
        };
        var handler = new AnthropicRetryHandler(
            new LanguageModelOptions { MaxRetries = 1, RetryBaseDelayMs = 1 },
            (delay, _) => { observedDelay = delay; return Task.CompletedTask; })
        {
            InnerHandler = stub
        };

        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(observedDelay);
        Assert.Equal(TimeSpan.FromSeconds(7), observedDelay);
    }

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses;

        public SequencedHandler(params HttpStatusCode[] statuses)
        {
            _statuses = new Queue<HttpStatusCode>(statuses);
        }

        public int CallCount { get; private set; }

        public List<string> ObservedBodies { get; } = new();

        public int? RetryAfterSeconds { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
            {
                ObservedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
            var response = new HttpResponseMessage(status);
            if (RetryAfterSeconds is { } seconds && (int)status == 429)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(seconds));
            }

            return response;
        }
    }
}

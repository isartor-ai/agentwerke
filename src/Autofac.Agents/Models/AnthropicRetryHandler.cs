using System.Net;
using Microsoft.Extensions.Options;

namespace Autofac.Agents.Models;

/// <summary>
/// Adds bounded, jittered exponential-backoff retries to the Anthropic HTTP pipeline for
/// transient failures only — HTTP 429 (rate limit), 529 (overloaded), and 5xx. Non-transient
/// 4xx responses (auth, validation) are returned immediately and never retried. A present
/// <c>Retry-After</c> header takes precedence over the computed backoff.
/// </summary>
public sealed class AnthropicRetryHandler : DelegatingHandler
{
    // 529 "overloaded_error" is Anthropic-specific and not in the HttpStatusCode enum.
    private const int OverloadedStatusCode = 529;

    private readonly LanguageModelOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public AnthropicRetryHandler(IOptions<LanguageModelOptions> options)
        : this(options.Value, static (delay, ct) => Task.Delay(delay, ct))
    {
    }

    // Test seam: lets unit tests inject a no-op delay so backoff is exercised without sleeping.
    internal AnthropicRetryHandler(LanguageModelOptions options, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _options = options;
        _delay = delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _options.MaxRetries);

        // Buffer the request body up front so the request can be replayed on retry; a request's
        // content stream is otherwise consumed by the first send.
        byte[]? bufferedContent = null;
        if (maxRetries > 0 && request.Content is not null)
        {
            bufferedContent = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        for (var attempt = 0; ; attempt++)
        {
            var attemptRequest = attempt == 0
                ? request
                : CloneRequest(request, bufferedContent);

            var response = await base.SendAsync(attemptRequest, cancellationToken);

            if (attempt >= maxRetries || !IsTransient(response.StatusCode))
            {
                return response;
            }

            var delay = ComputeDelay(attempt, response);
            response.Dispose();

            if (attempt > 0)
            {
                attemptRequest.Dispose();
            }

            await _delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode == HttpStatusCode.TooManyRequests
            || code == OverloadedStatusCode
            || code >= 500;
    }

    private TimeSpan ComputeDelay(int attempt, HttpResponseMessage response)
    {
        if (TryGetRetryAfter(response, out var retryAfter))
        {
            return retryAfter;
        }

        // Exponential backoff (base * 2^attempt) with full jitter, clamped to the configured max.
        var baseMs = Math.Max(1, _options.RetryBaseDelayMs);
        var maxMs = Math.Max(baseMs, _options.RetryMaxDelayMs);
        var exponential = baseMs * Math.Pow(2, attempt);
        var capped = Math.Min(exponential, maxMs);
        var jittered = Random.Shared.NextDouble() * capped;
        return TimeSpan.FromMilliseconds(jittered);
    }

    private static bool TryGetRetryAfter(HttpResponseMessage response, out TimeSpan delay)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                delay = delta;
                return true;
            }

            if (retryAfter.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero)
                {
                    delay = until;
                    return true;
                }
            }
        }

        delay = default;
        return false;
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request, byte[]? bufferedContent)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        if (bufferedContent is not null)
        {
            var content = new ByteArrayContent(bufferedContent);
            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            clone.Content = content;
        }

        return clone;
    }
}

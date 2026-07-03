using Agentwerke.Application.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Agentwerke.Observability;

/// <summary>
/// Reads or generates an <c>X-Correlation-Id</c> header for every request and
/// wires it into the scoped <see cref="CorrelationContext"/> so downstream
/// services can attach it to logs, events, and audit records.
/// </summary>
public sealed class CorrelationMiddleware : IMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? context.TraceIdentifier;

        context.Response.Headers[HeaderName] = correlationId;

        var correlationContext = context.RequestServices.GetRequiredService<CorrelationContext>();
        correlationContext.CorrelationId = correlationId;

        using var logScope = CreateLogScope(context, correlationId);
        await next(context);
    }

    private static IDisposable? CreateLogScope(HttpContext context, string correlationId)
    {
        var loggerFactory = context.RequestServices.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger<CorrelationMiddleware>();
        return logger?.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestPath"] = context.Request.Path.Value ?? string.Empty
        });
    }
}

using Agentwerke.Application.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Agentwerke.Observability;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentwerkeObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var tracingOptions = configuration
            .GetSection(TracingOptions.SectionName)
            .Get<TracingOptions>() ?? new TracingOptions();

        // Correlation context — scoped so one instance lives per HTTP request.
        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());
        services.AddTransient<CorrelationMiddleware>();
        services.AddHttpContextAccessor();

        // Metrics — singleton Meter, exported via Prometheus on /metrics.
        services.AddSingleton<WorkflowMetrics>();
        services.AddSingleton<IWorkflowMetrics>(sp => sp.GetRequiredService<WorkflowMetrics>());

        // Tracer — singleton ActivitySource wrapper.
        services.AddSingleton<IWorkflowTracer, WorkflowTracer>();

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: tracingOptions.ServiceName,
                serviceVersion: tracingOptions.ServiceVersion);

        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resourceBuilder)
                .AddMeter(WorkflowMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter())
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddSource(WorkflowActivitySource.SourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            if (request.Headers.TryGetValue(CorrelationMiddleware.HeaderName, out var corrId))
                            {
                                activity.SetTag("agentwerke.correlation_id", corrId.ToString());
                            }
                        };
                    })
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(tracingOptions.OtlpEndpoint))
                {
                    tracing.AddOtlpExporter(otlp =>
                        otlp.Endpoint = new Uri(tracingOptions.OtlpEndpoint));
                }
            });

        return services;
    }

    public static IApplicationBuilder UseAgentwerkeObservability(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationMiddleware>();
        return app;
    }

    public static ILoggingBuilder AddAgentwerkeLogging(this ILoggingBuilder logging)
    {
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        return logging;
    }
}

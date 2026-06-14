using Autofac.Application.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;

namespace Autofac.Observability;

public static class DependencyInjection
{
    /// <summary>
    /// Registers observability infrastructure: correlation context, metrics, and
    /// OpenTelemetry/Prometheus instrumentation. Call before <c>app.Build()</c>.
    /// </summary>
    public static IServiceCollection AddAutofacObservability(this IServiceCollection services)
    {
        // Correlation context — scoped so one instance lives per HTTP request.
        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());
        services.AddTransient<CorrelationMiddleware>();
        services.AddHttpContextAccessor();

        // Metrics — singleton Meter, exported via Prometheus on /metrics.
        services.AddSingleton<WorkflowMetrics>();
        services.AddSingleton<IWorkflowMetrics>(sp => sp.GetRequiredService<WorkflowMetrics>());

        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(WorkflowMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        return services;
    }

    /// <summary>
    /// Wires JSON console logging and the correlation middleware into the pipeline.
    /// Call after <c>app.Build()</c> and before route mapping.
    /// </summary>
    public static IApplicationBuilder UseAutofacObservability(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationMiddleware>();
        return app;
    }

    public static ILoggingBuilder AddAutofacLogging(this ILoggingBuilder logging)
    {
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        return logging;
    }
}

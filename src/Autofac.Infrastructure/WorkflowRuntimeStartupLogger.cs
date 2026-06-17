using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Autofac.Infrastructure;

/// <summary>
/// Emits a single startup log line announcing the active workflow runtime mode so the
/// effective runtime boundary is visible in diagnostics.
/// </summary>
public sealed class WorkflowRuntimeStartupLogger : IHostedService
{
    private readonly WorkflowRuntimeOptions _options;
    private readonly ILogger<WorkflowRuntimeStartupLogger> _logger;

    public WorkflowRuntimeStartupLogger(
        WorkflowRuntimeOptions options,
        ILogger<WorkflowRuntimeStartupLogger> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Mode == WorkflowRuntimeMode.Camunda)
        {
            _logger.LogInformation(
                "Autofac workflow runtime mode: {Mode}. Camunda adapter paths are active.",
                _options.Mode);
        }
        else
        {
            _logger.LogInformation(
                "Autofac workflow runtime mode: {Mode}. Camunda adapter paths are disabled.",
                _options.Mode);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

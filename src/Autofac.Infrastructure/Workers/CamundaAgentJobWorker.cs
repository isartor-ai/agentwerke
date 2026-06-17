using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofac.Infrastructure.Workers;

public sealed class CamundaAgentJobWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<CamundaOptions> _options;
    private readonly ILogger<CamundaAgentJobWorker> _logger;

    public CamundaAgentJobWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<CamundaOptions> options,
        ILogger<CamundaAgentJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.IsConfigured)
        {
            _logger.LogInformation("CamundaAgentJobWorker is disabled because Camunda is not fully configured.");
            return;
        }

        _logger.LogInformation("CamundaAgentJobWorker starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<CamundaAgentJobDispatcher>();
                var processed = await dispatcher.ProcessNextBatchAsync(stoppingToken);

                if (processed == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CamundaAgentJobWorker dispatch loop");
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("CamundaAgentJobWorker stopped.");
    }
}

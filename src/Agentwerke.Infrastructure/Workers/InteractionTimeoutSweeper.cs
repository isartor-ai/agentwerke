using Agentwerke.Application.Agents;
using Agentwerke.Application.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentwerke.Infrastructure.Workers;

/// <summary>Expires pending agent interactions after their explicit timeout.</summary>
public sealed class InteractionTimeoutSweeper : BackgroundService
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InteractionTimeoutSweeper> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _pollInterval;

    public InteractionTimeoutSweeper(
        IServiceScopeFactory scopeFactory,
        ILogger<InteractionTimeoutSweeper> logger,
        IOptions<InteractionOptions> options,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider;
        _pollInterval = options.Value.SweepIntervalSeconds > 0
            ? TimeSpan.FromSeconds(options.Value.SweepIntervalSeconds)
            : DefaultPollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "InteractionTimeoutSweeper starting. PollIntervalSeconds={PollIntervalSeconds}",
            _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in InteractionTimeoutSweeper loop");
            }

            await Task.Delay(_pollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("InteractionTimeoutSweeper stopped");
    }

    /// <summary>Executes one complete, directly testable sweep without delaying.</summary>
    public async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var interactions = scope.ServiceProvider.GetRequiredService<IAgentInteractionRepository>();
        var orchestration = scope.ServiceProvider.GetRequiredService<IWorkflowRunOrchestrationService>();
        var nowIso = _timeProvider.GetUtcNow().ToString("o");
        var due = await interactions.GetDueForExpiryAsync(nowIso, cancellationToken);

        // Every API instance may sweep the same row. The repository concurrency token elects exactly
        // one winner, so no leader election is needed and losing instances simply continue.
        foreach (var interaction in due)
        {
            try
            {
                await orchestration.ExpireInteractionAsync(interaction.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InteractionNotPendingException)
            {
                _logger.LogDebug(
                    "Timeout sweep lost terminal transition race. InteractionId={InteractionId}",
                    interaction.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to expire interaction {InteractionId}; continuing timeout batch",
                    interaction.Id);
            }
        }
    }
}

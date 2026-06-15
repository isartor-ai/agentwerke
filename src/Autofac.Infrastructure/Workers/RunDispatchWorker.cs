using Autofac.Application.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Autofac.Infrastructure.Workers;

public sealed class RunDispatchWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RunDispatchWorker> _logger;
    private readonly string _workerId = $"worker_{Environment.MachineName}_{Guid.NewGuid():N}";

    public RunDispatchWorker(IServiceScopeFactory scopeFactory, ILogger<RunDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RunDispatchWorker starting. WorkerId={WorkerId}", _workerId);

        await RecoverStuckRunsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RunDispatchWorker dispatch loop");
            }

            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("RunDispatchWorker stopped. WorkerId={WorkerId}", _workerId);
    }

    private async Task RecoverStuckRunsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IRunOutbox>();

            var stuckRunIds = await outbox.ListStuckRunIdsAsync(ct);
            if (stuckRunIds.Count == 0)
                return;

            _logger.LogWarning("Found {Count} stuck runs — re-enqueueing for recovery", stuckRunIds.Count);

            foreach (var runId in stuckRunIds)
            {
                await outbox.EnqueueAsync(OutboxOperations.Recover, runId, ct: ct);
                _logger.LogInformation("Re-enqueued recovery for stuck run {RunId}", runId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during startup recovery scan");
        }
    }

    private async Task DispatchNextAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IRunOutbox>();

        var entry = await outbox.TryClaimNextAsync(_workerId, ct);
        if (entry is null)
            return;

        _logger.LogInformation(
            "Dispatching outbox entry {EntryId} op={Operation} runId={RunId}",
            entry.Id, entry.Operation, entry.RunId);

        var executor = scope.ServiceProvider.GetRequiredService<IWorkflowRunExecutor>();

        try
        {
            switch (entry.Operation)
            {
                case OutboxOperations.Start:
                    var startPayload = OutboxStartPayload.Deserialize(entry.Payload);
                    await executor.ExecuteStartAsync(
                        entry.RunId,
                        startPayload?.WorkflowId ?? string.Empty,
                        startPayload?.Initiator,
                        startPayload?.CorrelationId,
                        ct);
                    break;

                case OutboxOperations.Resume:
                    var resumePayload = OutboxResumePayload.Deserialize(entry.Payload);
                    await executor.ExecuteResumeAsync(entry.RunId, resumePayload?.ApprovedBy, ct);
                    break;

                case OutboxOperations.Recover:
                case OutboxOperations.Timer:
                    await executor.ExecuteRecoverAsync(entry.RunId, ct);
                    break;

                default:
                    _logger.LogWarning("Unknown outbox operation '{Op}' for entry {EntryId}", entry.Operation, entry.Id);
                    break;
            }

            await outbox.MarkCompletedAsync(entry.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to execute outbox entry {EntryId} op={Operation} runId={RunId}",
                entry.Id, entry.Operation, entry.RunId);
            await outbox.MarkFailedAsync(entry.Id, ex.Message, ct);
        }
    }
}

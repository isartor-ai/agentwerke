using Agentwerke.Domain.Persistence;

namespace Agentwerke.Application.Workflows;

/// <summary>
/// Persists normalized external lifecycle events parsed from inbound webhooks
/// (GitHub pull request merges, workflow_run/check_suite completions). Implemented
/// in Agentwerke.Infrastructure. See <see cref="ExternalWorkflowEvent"/>.
/// </summary>
public interface IExternalWorkflowEventRepository
{
    Task AddAsync(ExternalWorkflowEvent @event, CancellationToken cancellationToken);

    /// <summary>
    /// Records an event unless one with the same <see cref="ExternalWorkflowEvent.DeliveryId"/>
    /// already exists, returning false when the delivery was a duplicate (#206). Callers use the
    /// result to decide whether to resume a run, so this must stay atomic: the unique index on
    /// DeliveryId is what makes a concurrent redelivery lose the race rather than resume twice.
    /// </summary>
    Task<bool> TryAddAsync(ExternalWorkflowEvent @event, CancellationToken cancellationToken);
}

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
}

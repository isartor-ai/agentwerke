namespace Autofac.Infrastructure;

/// <summary>
/// Placeholder Camunda status service used when the workflow runtime is not in Camunda mode.
/// It reports an inactive status without touching any Camunda configuration, client, or worker code.
/// </summary>
public sealed class DisabledCamundaRuntimeStatusService : ICamundaRuntimeStatusService
{
    public const string InactiveMessage =
        "Camunda runtime mode is not active. Set WorkflowRuntime:Mode=Camunda to enable the Camunda adapter.";

    public Task<CamundaRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CamundaRuntimeStatus(
            Enabled: false,
            Configured: false,
            Reachable: false,
            BaseUrl: null,
            AuthMode: CamundaAuthMode.None,
            GatewayVersion: null,
            BrokerCount: null,
            ClusterSize: null,
            PartitionsCount: null,
            ReplicationFactor: null,
            Error: InactiveMessage));
    }
}

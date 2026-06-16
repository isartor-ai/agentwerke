using Microsoft.Extensions.Options;

namespace Autofac.Infrastructure;

public sealed record CamundaRuntimeStatus(
    bool Enabled,
    bool Configured,
    bool Reachable,
    string? BaseUrl,
    CamundaAuthMode AuthMode,
    string? GatewayVersion,
    int? BrokerCount,
    int? ClusterSize,
    int? PartitionsCount,
    int? ReplicationFactor,
    string? Error);

public interface ICamundaRuntimeStatusService
{
    Task<CamundaRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed class CamundaRuntimeStatusService : ICamundaRuntimeStatusService
{
    private readonly ICamundaClient _camundaClient;
    private readonly IOptions<CamundaOptions> _options;

    public CamundaRuntimeStatusService(
        ICamundaClient camundaClient,
        IOptions<CamundaOptions> options)
    {
        _camundaClient = camundaClient;
        _options = options;
    }

    public async Task<CamundaRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var normalizedBaseUrl = NormalizeBaseUrl(options.BaseUrl);

        if (!options.IsConfigured)
        {
            return new CamundaRuntimeStatus(
                Enabled: options.Enabled,
                Configured: false,
                Reachable: false,
                BaseUrl: normalizedBaseUrl,
                AuthMode: options.AuthMode,
                GatewayVersion: null,
                BrokerCount: null,
                ClusterSize: null,
                PartitionsCount: null,
                ReplicationFactor: null,
                Error: options.Enabled
                    ? "Camunda runtime is enabled but configuration is incomplete."
                    : null);
        }

        try
        {
            var topology = await _camundaClient.GetTopologyAsync(cancellationToken);

            return new CamundaRuntimeStatus(
                Enabled: options.Enabled,
                Configured: true,
                Reachable: true,
                BaseUrl: normalizedBaseUrl,
                AuthMode: options.AuthMode,
                GatewayVersion: string.IsNullOrWhiteSpace(topology.GatewayVersion)
                    ? null
                    : topology.GatewayVersion,
                BrokerCount: topology.Brokers.Count,
                ClusterSize: topology.ClusterSize,
                PartitionsCount: topology.PartitionsCount,
                ReplicationFactor: topology.ReplicationFactor,
                Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CamundaRuntimeStatus(
                Enabled: options.Enabled,
                Configured: true,
                Reachable: false,
                BaseUrl: normalizedBaseUrl,
                AuthMode: options.AuthMode,
                GatewayVersion: null,
                BrokerCount: null,
                ClusterSize: null,
                PartitionsCount: null,
                ReplicationFactor: null,
                Error: ex.Message);
        }
    }

    private static string? NormalizeBaseUrl(string? baseUrl)
    {
        return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl;
    }
}

using System.Text.Json.Serialization;

namespace Agentwerke.Infrastructure;

public sealed class CamundaTopologyResponse
{
    public List<CamundaBrokerResponse> Brokers { get; set; } = [];

    public int ClusterSize { get; set; }

    [JsonPropertyName("partitionsCount")]
    public int PartitionsCount { get; set; }

    public int ReplicationFactor { get; set; }

    public string GatewayVersion { get; set; } = string.Empty;
}

public sealed class CamundaBrokerResponse
{
    public int NodeId { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public List<CamundaPartitionResponse> Partitions { get; set; } = [];

    public string Version { get; set; } = string.Empty;
}

public sealed class CamundaPartitionResponse
{
    public int PartitionId { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Health { get; set; } = string.Empty;
}

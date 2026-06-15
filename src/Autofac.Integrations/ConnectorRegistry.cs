namespace Autofac.Integrations;

public sealed class ConnectorRegistry : IConnectorRegistry
{
    private readonly IReadOnlyList<IConnector> _connectors;

    public ConnectorRegistry(IEnumerable<IConnector> connectors)
    {
        _connectors = connectors.ToArray();
    }

    public IReadOnlyList<ConnectorDescriptor> List()
    {
        return _connectors
            .Select(connector => new ConnectorDescriptor(
                connector.ConnectorId,
                connector.DisplayName,
                connector.Enabled,
                connector.SupportedOperations))
            .OrderBy(descriptor => descriptor.ConnectorId, StringComparer.Ordinal)
            .ToArray();
    }

    public IConnector? Find(string connectorId)
    {
        return _connectors.FirstOrDefault(connector => string.Equals(connector.ConnectorId, connectorId, StringComparison.Ordinal));
    }
}

using Autofac.Integrations;

namespace Autofac.Integrations.Tests;

public sealed class ConnectorRegistryTests
{
    [Fact]
    public void List_ReturnsRegisteredConnectorDescriptors()
    {
        var registry = new ConnectorRegistry(
        [
            new StubConnector("github", "GitHub", true, ["create_pull_request"]),
            new StubConnector("slack", "Slack", false, ["send_notification"])
        ]);

        var descriptors = registry.List();

        Assert.Equal(2, descriptors.Count);
        Assert.Contains(descriptors, static descriptor => descriptor.ConnectorId == "github" && descriptor.Enabled);
        Assert.Contains(descriptors, static descriptor => descriptor.ConnectorId == "slack" && !descriptor.Enabled);
    }

    private sealed class StubConnector : IConnector
    {
        public StubConnector(string connectorId, string displayName, bool enabled, IReadOnlyList<string> supportedOperations)
        {
            ConnectorId = connectorId;
            DisplayName = displayName;
            Enabled = enabled;
            SupportedOperations = supportedOperations;
        }

        public string ConnectorId { get; }
        public string DisplayName { get; }
        public bool Enabled { get; }
        public IReadOnlyList<string> SupportedOperations { get; }

        public Task<ConnectorExecutionResult> ExecuteAsync(ConnectorExecutionRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}

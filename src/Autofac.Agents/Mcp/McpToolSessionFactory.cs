using Autofac.Agents.Tools;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Mcp;

public sealed class McpToolSessionFactory : IMcpToolSessionFactory
{
    private readonly IMcpClientFactory _clientFactory;

    public McpToolSessionFactory(IMcpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<McpToolSessionResult> CreateAsync(
        IReadOnlyList<AgentMcpServerContract> servers,
        CancellationToken cancellationToken)
    {
        var enabledServers = (servers ?? [])
            .Where(static server => server.Enabled)
            .ToArray();

        if (enabledServers.Length == 0)
        {
            return new McpToolSessionResult(true, EmptyMcpToolSession.Instance, null);
        }

        var connections = new List<IMcpClientConnection>();
        var tools = new List<IAgentTool>();

        try
        {
            foreach (var server in enabledServers)
            {
                var connection = await _clientFactory.CreateAsync(server, cancellationToken);
                connections.Add(connection);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, server.StartupTimeoutSeconds)));

                await connection.InitializeAsync(timeoutCts.Token);
                var discoveredTools = await connection.ListToolsAsync(timeoutCts.Token);

                foreach (var discoveredTool in discoveredTools)
                {
                    tools.Add(new McpAgentTool(
                        qualifiedName: BuildQualifiedToolName(server.Name, discoveredTool.Name),
                        serverToolName: discoveredTool.Name,
                        connection,
                        discoveredTool.InputSchema));
                }
            }

            return new McpToolSessionResult(
                true,
                new McpToolSession(connections, tools),
                null);
        }
        catch (Exception ex)
        {
            foreach (var connection in connections)
            {
                await connection.DisposeAsync();
            }

            return new McpToolSessionResult(
                false,
                null,
                $"MCP startup failed: {ex.Message}");
        }
    }

    internal static string BuildQualifiedToolName(string serverName, string toolName) =>
        $"mcp.{serverName}.{toolName}";

    private sealed class McpToolSession : IMcpToolSession
    {
        private readonly IReadOnlyList<IMcpClientConnection> _connections;

        public McpToolSession(
            IReadOnlyList<IMcpClientConnection> connections,
            IReadOnlyList<IAgentTool> tools)
        {
            _connections = connections;
            Tools = tools;
        }

        public IReadOnlyList<IAgentTool> Tools { get; }

        public async ValueTask DisposeAsync()
        {
            foreach (var connection in _connections)
            {
                await connection.DisposeAsync();
            }
        }
    }

    private sealed class EmptyMcpToolSession : IMcpToolSession
    {
        public static readonly EmptyMcpToolSession Instance = new();

        public IReadOnlyList<IAgentTool> Tools => [];

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

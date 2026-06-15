using System.Text.Json;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Mcp;

public sealed record McpToolDefinition(
    string Name,
    string? Title,
    string? Description,
    JsonElement? InputSchema);

public sealed record McpToolCallResult(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    IReadOnlyDictionary<string, string>? Artifacts = null);

public interface IMcpClientConnection : IAsyncDisposable
{
    string ServerName { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken);

    Task<McpToolCallResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken);
}

public interface IMcpClientFactory
{
    Task<IMcpClientConnection> CreateAsync(AgentMcpServerContract server, CancellationToken cancellationToken);
}

public interface IMcpToolSession : IAsyncDisposable
{
    IReadOnlyList<Autofac.Agents.Tools.IAgentTool> Tools { get; }
}

public sealed record McpToolSessionResult(
    bool Succeeded,
    IMcpToolSession? Session,
    string? FailureReason);

public interface IMcpToolSessionFactory
{
    Task<McpToolSessionResult> CreateAsync(
        IReadOnlyList<AgentMcpServerContract> servers,
        CancellationToken cancellationToken);
}

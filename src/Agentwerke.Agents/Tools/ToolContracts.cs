using Agentwerke.Domain.Persistence;
using Agentwerke.Workflows.Runtime;

namespace Agentwerke.Agents.Tools;

public interface IToolSchemaProvider
{
    IReadOnlyList<ToolSchemaParameter> GetParameters();
}

public sealed record ToolSchemaParameter(
    string Name,
    string Type,
    string Description,
    bool Required = false,
    /// <summary>Allowed values for an enum-constrained parameter, or null/empty for none.</summary>
    IReadOnlyList<string>? EnumValues = null,
    /// <summary>For <c>Type == "array"</c>, the JSON schema type of each item (e.g. "string").</summary>
    string? ItemType = null);

public interface IAgentTool
{
    string Name { get; }

    string Category { get; }

    void Validate(IReadOnlyDictionary<string, string> input);

    Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken);
}

public sealed record AgentToolExecutionContext(
    string RunId,
    string StepId,
    string AgentName,
    string Action,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    int Attempt);

public sealed record AgentToolExecutionResult(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    IReadOnlyDictionary<string, string>? Artifacts = null,
    IReadOnlyList<ExternalActionRecord>? ExternalActions = null,
    Domain.AgentRuntime.AgentSandboxExecutionRecord? SandboxExecution = null);

public sealed record ToolGatewayRequest(
    string ToolName,
    string Action,
    string RunId,
    string StepId,
    string AgentName,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    IReadOnlyList<string> RequiresEvidence,
    int Attempt,
    string PermissionLevel,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> DeniedTools,
    IReadOnlyDictionary<string, string> Input,
    /// <summary>
    /// Named sandbox profiles (see Agentwerke.Sandboxes.SandboxProfileCatalog) the requesting
    /// agent is declared allowed to use. Empty means offline-only. Only consulted for the
    /// "sandbox.execute" tool.
    /// </summary>
    IReadOnlyList<string>? AllowedSandboxProfiles = null,
    /// <summary>
    /// How a contract-denied tool call is handled (#202): "escalate" (default; asks a human via
    /// a blocking tool_access interaction) or "fail" (fail the tool call immediately).
    /// </summary>
    string? ToolEscalation = null);

public sealed record ToolGatewayResult(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    PolicyDecision? PolicyDecision,
    Domain.AgentRuntime.AgentToolInvocationRecord Invocation,
    IReadOnlyDictionary<string, string>? Artifacts = null,
    IReadOnlyList<ExternalActionRecord>? ExternalActions = null,
    Domain.AgentRuntime.AgentSandboxExecutionRecord? SandboxExecution = null);

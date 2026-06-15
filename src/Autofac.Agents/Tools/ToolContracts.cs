using Autofac.Domain.Persistence;
using Autofac.Workflows.Runtime;

namespace Autofac.Agents.Tools;

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
    IReadOnlyList<ExternalActionRecord>? ExternalActions = null);

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
    IReadOnlyDictionary<string, string> Input);

public sealed record ToolGatewayResult(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    PolicyDecision? PolicyDecision,
    Domain.AgentRuntime.AgentToolInvocationRecord Invocation,
    IReadOnlyDictionary<string, string>? Artifacts = null,
    IReadOnlyList<ExternalActionRecord>? ExternalActions = null);

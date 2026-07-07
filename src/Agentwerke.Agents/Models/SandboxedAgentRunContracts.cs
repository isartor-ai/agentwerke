using Agentwerke.Domain.AgentRuntime;

namespace Agentwerke.Agents.Models;

public sealed record SandboxedAgentRunEnvelope(
    string RunId,
    string StepId,
    string AgentName,
    string Action,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    int Attempt,
    string SystemPrompt,
    string UserPrompt,
    string Model,
    int MaxTokens,
    AgentRuntimeContract Contract,
    IReadOnlyList<SandboxedToolContract> ResolvedTools,
    IReadOnlyList<SandboxedSubAgentProfile> SubAgents,
    int RemainingSubAgentDepth);

public sealed record SandboxedToolContract(
    string Name,
    string Category);

public sealed record SandboxedSubAgentProfile(
    string AgentId,
    string Name,
    string Description,
    string? SystemPrompt,
    string? Model);

public sealed record SandboxedAgentRunResult(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    AgentModelTokenUsage? TokenUsage,
    IReadOnlyDictionary<string, string>? Artifacts = null,
    IReadOnlyList<AgentToolInvocationRecord>? ToolInvocations = null,
    AgentModelTraceRecord? ModelTrace = null);

public interface ISandboxedAgentRunner
{
    Task<ModelRunResult> RunAsync(
        ModelRunRequest request,
        AgentProfile? profile,
        string sandboxProfileName,
        CancellationToken cancellationToken);
}

using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Models;

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
    int MaxTokens);

public sealed record SandboxedAgentRunResult(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    AgentModelTokenUsage? TokenUsage,
    IReadOnlyDictionary<string, string>? Artifacts = null);

public interface ISandboxedAgentRunner
{
    Task<ModelRunResult> RunAsync(
        ModelRunRequest request,
        AgentProfile? profile,
        string sandboxProfileName,
        CancellationToken cancellationToken);
}

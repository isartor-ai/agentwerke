using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Workflows.Runtime;

namespace Agentwerke.Agents.Models;

public sealed class NullAgentModelRunner : IAgentModelRunner
{
    public Task<ModelRunResult> RunAsync(ModelRunRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ModelRunResult(
            Succeeded: false,
            Output: null,
            FailureReason: "No language model client is configured. Set 'Anthropic:ApiKey' in configuration.",
            ToolInvocations: [],
            Artifacts: null,
            TokenUsage: null,
            StepStatus: AgentTaskOutcomeStatuses.NeedsConfig));
}

public interface IAgentModelRunner
{
    Task<ModelRunResult> RunAsync(ModelRunRequest request, CancellationToken cancellationToken);
}

public sealed record ModelRunRequest(
    string RunId,
    string StepId,
    string AgentName,
    string Action,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    IReadOnlyList<string> RequiresEvidence,
    int Attempt,
    AgentPromptSnapshot PromptSnapshot,
    AgentRuntimeContract Contract);

public sealed record ModelRunResult(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    IReadOnlyList<AgentToolInvocationRecord> ToolInvocations,
    IReadOnlyDictionary<string, string>? Artifacts,
    AgentModelTokenUsage? TokenUsage,
    double ElapsedMs = 0,
    AgentSandboxExecutionRecord? SandboxExecution = null,
    string? StepStatus = null);

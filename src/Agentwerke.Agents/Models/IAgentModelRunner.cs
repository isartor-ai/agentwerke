using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Workflows.Runtime;

namespace Agentwerke.Agents.Models;

public sealed class NullAgentModelRunner : IAgentModelRunner
{
    public Task<ModelRunResult> RunAsync(
        ModelRunRequest request,
        CancellationToken cancellationToken,
        AgentExecutionProgressReporter? progressReporter = null) =>
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
    Task<ModelRunResult> RunAsync(
        ModelRunRequest request,
        CancellationToken cancellationToken,
        AgentExecutionProgressReporter? progressReporter = null);
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
    AgentRuntimeContract Contract,
    /// <summary>Per-agent model id override from the agent profile; null uses the configured default.</summary>
    string? Model = null,
    /// <summary>Per-agent reasoning effort from the agent profile; null sends nothing.</summary>
    string? ReasoningEffort = null,
    string? NodeId = null,
    int DelegationDepth = 0,
    IReadOnlyList<string>? DelegationChain = null);

public sealed record ModelRunResult(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    IReadOnlyList<AgentToolInvocationRecord> ToolInvocations,
    IReadOnlyDictionary<string, string>? Artifacts,
    AgentModelTokenUsage? TokenUsage,
    double ElapsedMs = 0,
    AgentSandboxExecutionRecord? SandboxExecution = null,
    string? StepStatus = null,
    AgentModelTraceRecord? ModelTrace = null);

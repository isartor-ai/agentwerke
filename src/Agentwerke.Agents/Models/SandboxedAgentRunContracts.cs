using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Workflows.Runtime;

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
    int RemainingSubAgentDepth,
    string? ReasoningEffort = null,
    /// <summary>
    /// Tools the sandboxed runtime supports but that are NOT allowed for this step. A call to
    /// one of these pauses the run on a tool-access interaction (#202) instead of failing.
    /// </summary>
    IReadOnlyList<string>? EscalatableTools = null,
    /// <summary>
    /// Operator guidance for tools whose access request was declined, keyed by tool name; the
    /// guidance is returned to the model as the tool result on the step re-run.
    /// </summary>
    IReadOnlyDictionary<string, string>? ToolAccessGuidance = null);

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
    AgentModelTraceRecord? ModelTrace = null,
    /// <summary><c>waiting_user</c> when the run must pause for a tool-access decision (#202).</summary>
    string? StepStatus = null,
    /// <summary>The interaction prompt the host must persist when <c>StepStatus</c> is waiting_user.</summary>
    string? PendingToolAccessPrompt = null,
    /// <summary>The requested tool, so the host can record it on the interaction (#202).</summary>
    string? PendingToolAccessToolName = null,
    /// <summary>The model's stated intent (truncated tool input) for the operator (#202).</summary>
    string? PendingToolAccessIntent = null);

/// <summary>
/// Raised inside the sandboxed model loop when the agent calls an escalatable tool (#202). The
/// sandbox has no database access, so the executor converts this into result-payload fields the
/// host persists as a tool-access interaction.
/// </summary>
public sealed class SandboxToolAccessRequiredException : Exception
{
    public SandboxToolAccessRequiredException(string prompt, string toolName, string intent)
        : base(prompt)
    {
        Prompt = prompt;
        ToolName = toolName;
        Intent = intent;
    }

    public string Prompt { get; }

    public string ToolName { get; }

    public string Intent { get; }
}

public interface ISandboxedAgentRunner
{
    Task<ModelRunResult> RunAsync(
        ModelRunRequest request,
        AgentProfile? profile,
        string sandboxProfileName,
        CancellationToken cancellationToken,
        AgentExecutionProgressReporter? progressReporter = null);
}

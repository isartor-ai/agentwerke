using Agentwerke.Domain.AgentRuntime;

namespace Agentwerke.Agents.Prompts;

public sealed record AgentPromptAssemblyResult(
    bool Succeeded,
    AgentPromptSnapshot PromptSnapshot,
    string? FailureReason = null,
    IReadOnlyList<string>? MissingVariables = null);

using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Prompts;

public sealed record AgentPromptAssemblyResult(
    bool Succeeded,
    AgentPromptSnapshot PromptSnapshot,
    string? FailureReason = null,
    IReadOnlyList<string>? MissingVariables = null);

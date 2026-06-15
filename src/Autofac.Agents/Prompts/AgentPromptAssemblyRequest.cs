using Autofac.Agents.Skills;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Prompts;

public sealed record AgentPromptAssemblyRequest(
    string RunId,
    string StepId,
    string NodeId,
    string? NodeName,
    string AgentName,
    string? AgentDescription,
    string? AgentCategory,
    string Action,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    int Attempt,
    IReadOnlyList<string> RequiresEvidence,
    AgentPromptContract? Prompt = null,
    SkillManifest? Skill = null);

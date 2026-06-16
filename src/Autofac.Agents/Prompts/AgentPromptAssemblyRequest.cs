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
    SkillManifest? Skill = null,
    /// <summary>
    /// Run-scoped context (triggering issue, prior step outputs) keyed by stable
    /// names like "input.body" / "output.&lt;nodeId&gt;". Exposed both as template
    /// variables and as a rendered "run_context" prompt section.
    /// </summary>
    IReadOnlyDictionary<string, string>? RunContext = null);

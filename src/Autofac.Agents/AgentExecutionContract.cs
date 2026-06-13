namespace Autofac.Agents;

// ── Execution request ─────────────────────────────────────────────────────────

public sealed record AgentExecutionRequest(
    string RunId,
    string StepId,
    string NodeId,
    string? NodeName,
    string AgentName,
    string Action,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    IReadOnlyList<string> RequiresEvidence,
    int Attempt);

// ── Execution result ──────────────────────────────────────────────────────────

public sealed record AgentExecutionResult(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    IReadOnlyDictionary<string, string>? Artifacts = null);

// ── Skill reference ───────────────────────────────────────────────────────────

public sealed record AgentSkillRef(
    string SkillId,
    string Name,
    string Description,
    IReadOnlyList<string> SupportedActions);

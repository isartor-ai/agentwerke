namespace Autofac.Api.Contracts.Runs;

public sealed record RunStepRuntimeSnapshot(
    string? AgentName,
    string? Action,
    string? PromptInline,
    IReadOnlyList<RunStepSkillUsage> Skills,
    IReadOnlyList<RunStepToolInfo> Tools,
    IReadOnlyList<string> McpServers,
    IReadOnlyList<RunStepHookExecution> Hooks,
    string PermissionLevel,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> DeniedTools,
    bool SubAgentsEnabled,
    RunStepPermissionDecision? PermissionDecision,
    IReadOnlyList<RunStepArtifactRef> StepArtifacts);

public sealed record RunStepSkillUsage(
    string SkillId,
    string? Name,
    bool Selected,
    string? Fingerprint);

public sealed record RunStepToolInfo(
    string Name,
    string Category);

public sealed record RunStepHookExecution(
    string Event,
    string Type,
    string Decision,
    int? DurationMs);

public sealed record RunStepPermissionDecision(
    string Level,
    bool Allowed,
    string? Rationale);

public sealed record RunStepArtifactRef(
    string Name,
    string? Uri,
    string? ContentType);

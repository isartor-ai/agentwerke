namespace Autofac.Api.Contracts.Runs;

public sealed record RunStepRuntimeSnapshot(
    string? AgentName,
    string? Action,
    string? PromptInline,
    PromptSnapshot? Prompt,
    IReadOnlyList<RunStepSkillUsage> Skills,
    IReadOnlyList<RunStepToolInfo> Tools,
    IReadOnlyList<RunStepToolInvocation> ToolInvocations,
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
    string? Fingerprint,
    bool Invoked = false,
    string Source = "agent-profile");

public sealed record RunStepToolInfo(
    string Name,
    string Category);

public sealed record RunStepToolInvocation(
    string ToolName,
    string Category,
    string Status,
    string? PolicyDecisionId,
    string? PolicyDecisionKind,
    string? InputSummary,
    string? OutputSummary,
    string? ErrorMessage,
    IReadOnlyList<string> ArtifactNames,
    int? DurationMs);

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

namespace Autofac.Api.Contracts.Runs;

public sealed record RunStep(
    string Id,
    string Name,
    string Type,
    string Status,
    string? StartedAt,
    string? CompletedAt,
    string? AgentName,
    string? Output,
    string? Error,
    PolicyDecision? PolicyDecision,
    PromptSnapshot? PromptSnapshot,
    IReadOnlyList<SkillAuditRecord> Skills,
    IReadOnlyList<ToolInvocationRecord> ToolInvocations,
    IReadOnlyList<HookExecutionRecord> HookExecutions,
    IReadOnlyList<SubAgentRunRecord> SubAgentRuns);

public sealed record SkillAuditRecord(
    string SkillId,
    string? Name,
    string? Description,
    string? Version,
    string? Fingerprint,
    IReadOnlyList<string> InvocationRules,
    IReadOnlyList<string> RequiredFiles,
    IReadOnlyList<string> OptionalTools,
    string Source,
    bool Available,
    bool Selected,
    bool Invoked);

public sealed record ToolInvocationRecord(
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

public sealed record HookExecutionRecord(
    string HookName,
    string Event,
    string Type,
    string Decision,
    bool Blocking,
    string? OutputSummary,
    string? ErrorMessage,
    int? DurationMs);

public sealed record SubAgentRunRecord(
    string RunId,
    string ParentRunId,
    string ParentStepId,
    string AgentName,
    string Action,
    string Status,
    int Depth,
    string PermissionLevel,
    string FailureBehavior,
    string CorrelationId,
    string StartedAt,
    string? CompletedAt,
    string? OutputSummary,
    string? FailureReason,
    IReadOnlyList<string> ArtifactNames,
    IReadOnlyList<string> EventMessages);

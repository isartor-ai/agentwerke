namespace Autofac.Domain.AgentRuntime;

public sealed record AgentRuntimeSnapshot
{
    public required string RunId { get; init; }

    public required string StepId { get; init; }

    public required string NodeId { get; init; }

    public string? AgentName { get; init; }

    public string? Action { get; init; }

    public AgentPromptSnapshot? Prompt { get; init; }

    public AgentRuntimeContract Contract { get; init; } = new();

    public IReadOnlyList<AgentSkillUsageRecord> Skills { get; init; } = [];

    public IReadOnlyList<AgentToolInvocationRecord> ToolInvocations { get; init; } = [];

    public IReadOnlyList<AgentHookExecutionRecord> HookExecutions { get; init; } = [];

    public IReadOnlyList<AgentArtifactRecord> Artifacts { get; init; } = [];

    public AgentPermissionDecisionRecord? PermissionDecision { get; init; }
}

public sealed record AgentPromptSnapshot(
    string FinalPrompt,
    string RenderedAt,
    IReadOnlyList<AgentPromptSectionSnapshot> Sections,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> SourceFiles);

public sealed record AgentPromptSectionSnapshot(
    string Name,
    string Content,
    string Source);

public sealed record AgentSkillUsageRecord
{
    public required string SkillId { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? Version { get; init; }

    public string? Fingerprint { get; init; }

    public IReadOnlyList<string> InvocationRules { get; init; } = [];

    public IReadOnlyList<string> RequiredFiles { get; init; } = [];

    public IReadOnlyList<string> OptionalTools { get; init; } = [];

    public string Source { get; init; } = "agent-profile";

    public bool Available { get; init; } = true;

    public bool Invoked { get; init; }

    public bool Selected { get; init; } = true;
}

public sealed record AgentToolInvocationRecord
{
    public required string ToolName { get; init; }

    public string Category { get; init; } = AgentToolCategories.Read;

    public string Status { get; init; } = "pending";

    public string? PolicyDecisionId { get; init; }

    public string? PolicyDecisionKind { get; init; }

    public string? InputSummary { get; init; }

    public string? OutputSummary { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<string> ArtifactNames { get; init; } = [];

    public int? DurationMs { get; init; }
}

public sealed record AgentHookExecutionRecord
{
    public required string HookName { get; init; }

    public required string Event { get; init; }

    public required string Type { get; init; }

    public string Decision { get; init; } = "proceed";

    public bool Blocking { get; init; }

    public string? OutputSummary { get; init; }

    public string? ErrorMessage { get; init; }

    public int? DurationMs { get; init; }
}

public sealed record AgentArtifactRecord
{
    public required string Name { get; init; }

    public string? Uri { get; init; }

    public string? ContentType { get; init; }
}

public sealed record AgentPermissionDecisionRecord
{
    public string Level { get; init; } = AgentPermissionLevels.ReadOnly;

    public bool Allowed { get; init; }

    public string? Rationale { get; init; }
}

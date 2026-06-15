namespace Autofac.Domain.AgentRuntime;

public sealed record AgentRuntimeContract
{
    public AgentPromptContract? Prompt { get; init; }

    public IReadOnlyList<AgentSkillContract> Skills { get; init; } = [];

    public IReadOnlyList<AgentToolContract> Tools { get; init; } = [];

    public IReadOnlyList<AgentMcpServerContract> McpServers { get; init; } = [];

    public IReadOnlyList<AgentHookContract> Hooks { get; init; } = [];

    public AgentSubAgentContract? SubAgents { get; init; }

    public AgentPermissionContract Permissions { get; init; } = AgentPermissionContract.ReadOnly;

    public AgentOutputContract Outputs { get; init; } = AgentOutputContract.Default;

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record AgentPromptContract
{
    public string? Inline { get; init; }

    public string? File { get; init; }

    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record AgentSkillContract
{
    public required string SkillId { get; init; }

    public string? Name { get; init; }

    public string? Version { get; init; }

    public bool Required { get; init; } = true;
}

public sealed record AgentToolContract
{
    public required string Name { get; init; }

    public string Category { get; init; } = AgentToolCategories.Read;

    public bool Required { get; init; } = true;
}

public static class AgentToolCategories
{
    public const string Read = "read";
    public const string Write = "write";
    public const string Shell = "shell";
    public const string Web = "web";
    public const string Integration = "integration";
    public const string Mcp = "mcp";
    public const string SubAgent = "sub-agent";
}

public sealed record AgentMcpServerContract
{
    public required string Name { get; init; }

    public string Transport { get; init; } = "stdio";

    public string? Command { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? Url { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public int StartupTimeoutSeconds { get; init; } = 15;

    public bool Enabled { get; init; } = true;
}

public sealed record AgentHookContract
{
    public required string Event { get; init; }

    public required string Type { get; init; }

    public bool Blocking { get; init; }

    public int TimeoutSeconds { get; init; } = 30;
}

public sealed record AgentSubAgentContract
{
    public bool Enabled { get; init; }

    public int MaxDepth { get; init; } = 1;

    public IReadOnlyList<string> AllowedAgents { get; init; } = [];
}

public sealed record AgentPermissionContract
{
    public static readonly AgentPermissionContract ReadOnly = new() { Level = AgentPermissionLevels.ReadOnly };

    public string Level { get; init; } = AgentPermissionLevels.ReadOnly;

    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    public IReadOnlyList<string> DeniedTools { get; init; } = [];
}

public static class AgentPermissionLevels
{
    public const string ReadOnly = "read-only";
    public const string ReadWrite = "read-write";
    public const string Full = "full";
}

public sealed record AgentOutputContract
{
    public static readonly AgentOutputContract Default = new();

    public bool CaptureResponse { get; init; } = true;

    public bool CaptureStatus { get; init; } = true;

    public bool CaptureArtifacts { get; init; } = true;

    public bool CaptureFilesTouched { get; init; }
}

namespace Autofac.Api.Contracts.Agents;

public sealed record AgentDetail(
    string AgentId,
    string Name,
    string Description,
    string Category,
    string Runner,
    string? Model,
    string? DockerImage,
    string Network,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> DeniedTools,
    IReadOnlyList<string> SupportedActions,
    IReadOnlyList<AgentSkillBinding> Skills,
    IReadOnlyList<string> SupportedEnvironments,
    IReadOnlyList<string> SupportedPolicyTags,
    IReadOnlyList<string> Secrets,
    string Source,
    string? Fingerprint,
    string? SystemPrompt,
    string RawMarkdown,
    string EffectiveFilePath,
    string? SourceFilePath);

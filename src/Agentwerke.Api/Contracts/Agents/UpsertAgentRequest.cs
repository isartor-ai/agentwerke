namespace Agentwerke.Api.Contracts.Agents;

public sealed record UpsertAgentRequest(
    string AgentId,
    string Name,
    string Description,
    string Category,
    string Runner,
    string? Model = null,
    string? DockerImage = null,
    string? Network = null,
    IReadOnlyList<string>? Tools = null,
    IReadOnlyList<string>? DeniedTools = null,
    IReadOnlyList<string>? SupportedActions = null,
    IReadOnlyList<AgentSkillBinding>? Skills = null,
    IReadOnlyList<string>? SupportedEnvironments = null,
    IReadOnlyList<string>? SupportedPolicyTags = null,
    IReadOnlyList<string>? Secrets = null,
    IReadOnlyList<string>? SandboxProfiles = null,
    string? SystemPrompt = null);

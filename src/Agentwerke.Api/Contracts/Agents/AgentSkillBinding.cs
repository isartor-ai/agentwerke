namespace Agentwerke.Api.Contracts.Agents;

public sealed record AgentSkillBinding(
    string SkillId,
    string Name,
    string Description,
    IReadOnlyList<string> SupportedActions,
    string? SkillManifestId);

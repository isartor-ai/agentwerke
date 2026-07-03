namespace Agentwerke.Api.Contracts.Templates;

public sealed record TemplateSummary(
    string Id,
    string Name,
    string Description,
    string Trigger,
    string PolicyLevel,
    string[] Tags,
    string[] AgentRoles,
    string[] ApprovalRoles);

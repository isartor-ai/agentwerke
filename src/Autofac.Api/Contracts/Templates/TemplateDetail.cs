namespace Autofac.Api.Contracts.Templates;

public sealed record TemplateDetail(
    string Id,
    string Name,
    string Description,
    string Trigger,
    string PolicyLevel,
    string[] Tags,
    string[] AgentRoles,
    string[] ApprovalRoles,
    string[] RequiredInputs,
    string[] EvidenceExpectations,
    string BpmnXml);

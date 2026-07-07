namespace Agentwerke.Api.Contracts.Workflows;

public sealed record ImportWorkflowRequest(
    string FileName,
    string BpmnXml,
    string? Description = null,
    IReadOnlyList<string>? Tags = null);

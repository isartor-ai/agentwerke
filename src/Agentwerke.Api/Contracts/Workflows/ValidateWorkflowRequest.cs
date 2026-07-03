namespace Agentwerke.Api.Contracts.Workflows;

public sealed record ValidateWorkflowRequest(string? WorkflowId, string BpmnXml);

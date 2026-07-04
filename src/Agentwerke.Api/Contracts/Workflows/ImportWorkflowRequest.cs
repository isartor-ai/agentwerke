namespace Agentwerke.Api.Contracts.Workflows;

public sealed record ImportWorkflowRequest(string FileName, string BpmnXml);

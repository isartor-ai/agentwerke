namespace Autofac.Api.Contracts.Workflows;

public sealed record PublishWorkflowRequest(string BpmnXml, string? Description = null);

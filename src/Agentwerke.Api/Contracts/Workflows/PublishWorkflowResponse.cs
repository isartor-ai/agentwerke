namespace Agentwerke.Api.Contracts.Workflows;

public sealed record PublishWorkflowResponse(
    string WorkflowId,
    string Version,
    string PublishedAt);

namespace Agentwerke.Api.Contracts.Workflows;

public sealed record ImportWorkflowResponse(string WorkflowId, ValidationResponse Validation);

namespace Autofac.Api.Contracts.Workflows;

public sealed record CamundaWorkflowDeployment(
    string DeploymentKey,
    string ProcessDefinitionId,
    string ProcessDefinitionKey,
    int ProcessDefinitionVersion,
    string DeployedAt);

namespace Autofac.Api.Contracts.Runs;

public sealed record CamundaRunLink(
    string ProcessInstanceKey,
    string ProcessDefinitionKey,
    string ProcessDefinitionId,
    int ProcessDefinitionVersion);

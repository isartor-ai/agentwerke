namespace Autofac.Api.Contracts.Runs;

public sealed record StartRunResponse(string RunId, string WorkflowId, string Status);

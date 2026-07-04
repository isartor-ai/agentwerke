namespace Agentwerke.Api.Contracts.Runs;

public sealed record StartRunRequest(
    string WorkflowId,
    IReadOnlyDictionary<string, string>? Inputs = null);

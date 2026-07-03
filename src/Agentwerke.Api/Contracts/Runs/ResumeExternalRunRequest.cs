namespace Agentwerke.Api.Contracts.Runs;

public sealed record ResumeExternalRunRequest(
    string CorrelationKey,
    IReadOnlyDictionary<string, string>? Payload,
    string? ResumedBy);

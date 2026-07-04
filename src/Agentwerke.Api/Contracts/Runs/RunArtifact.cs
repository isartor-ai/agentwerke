namespace Agentwerke.Api.Contracts.Runs;

public sealed record RunArtifact(
    string Name,
    long SizeBytes,
    string LastModifiedAt);

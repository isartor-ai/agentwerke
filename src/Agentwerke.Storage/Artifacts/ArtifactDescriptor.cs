namespace Agentwerke.Storage.Artifacts;

public sealed record ArtifactDescriptor(
    string Name,
    long SizeBytes,
    string LastModifiedAt);

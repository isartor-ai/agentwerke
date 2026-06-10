namespace Autofac.Storage.Artifacts;

public interface IArtifactStorage
{
    Task SaveAsync(
        string runId,
        string artifactName,
        Stream content,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(
        string runId,
        string artifactName,
        CancellationToken cancellationToken);

    Task<bool> ExistsAsync(
        string runId,
        string artifactName,
        CancellationToken cancellationToken);
}

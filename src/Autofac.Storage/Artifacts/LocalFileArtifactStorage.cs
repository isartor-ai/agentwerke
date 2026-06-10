using Microsoft.Extensions.Options;

namespace Autofac.Storage.Artifacts;

public sealed class LocalFileArtifactStorage : IArtifactStorage
{
    private readonly string _rootPath;

    public LocalFileArtifactStorage(IOptions<StorageOptions> options)
    {
        _rootPath = Path.GetFullPath(options.Value.RootPath);
    }

    public async Task SaveAsync(
        string runId,
        string artifactName,
        Stream content,
        CancellationToken cancellationToken)
    {
        var path = BuildPath(runId, artifactName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var fileStream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(fileStream, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(
        string runId,
        string artifactName,
        CancellationToken cancellationToken)
    {
        var path = BuildPath(runId, artifactName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Artifact not found.", path);
        }

        var memory = new MemoryStream();
        await using var fileStream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        await fileStream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }

    public Task<bool> ExistsAsync(
        string runId,
        string artifactName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = BuildPath(runId, artifactName);
        return Task.FromResult(File.Exists(path));
    }

    private string BuildPath(string runId, string artifactName)
    {
        var safeRunId = Sanitize(runId);
        var safeName = Sanitize(artifactName);
        return Path.Combine(_rootPath, safeRunId, safeName);
    }

    private static string Sanitize(string input)
    {
        var safe = input.Trim();

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '_');
        }

        safe = safe.Replace("/", "_").Replace("\\", "_").Replace("..", "_");

        if (string.IsNullOrWhiteSpace(safe))
        {
            throw new ArgumentException("Value cannot be empty after sanitization.", nameof(input));
        }

        return safe;
    }
}

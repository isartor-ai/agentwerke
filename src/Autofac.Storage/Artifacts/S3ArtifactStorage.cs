using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Autofac.Storage.Artifacts;

public sealed class S3ArtifactStorage : IArtifactStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;

    public S3ArtifactStorage(IAmazonS3 s3, IOptions<StorageOptions> options)
    {
        _s3 = s3;
        _bucketName = options.Value.S3.BucketName;
    }

    public async Task<IReadOnlyList<ArtifactDescriptor>> ListAsync(string runId, CancellationToken cancellationToken)
    {
        var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = BuildPrefix(runId)
        }, cancellationToken);

        return response.S3Objects
            .Select(item => new ArtifactDescriptor(
                Path.GetFileName(item.Key),
                item.Size ?? 0L,
                item.LastModified?.ToUniversalTime().ToString("o") ?? string.Empty))
            .ToArray();
    }

    public async Task SaveAsync(string runId, string artifactName, Stream content, CancellationToken cancellationToken)
    {
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = BuildKey(runId, artifactName),
            InputStream = content
        }, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string runId, string artifactName, CancellationToken cancellationToken)
    {
        var response = await _s3.GetObjectAsync(_bucketName, BuildKey(runId, artifactName), cancellationToken);
        var memory = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }

    public async Task<bool> ExistsAsync(string runId, string artifactName, CancellationToken cancellationToken)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_bucketName, BuildKey(runId, artifactName), cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static string BuildPrefix(string runId) => $"{Sanitize(runId)}/";

    private static string BuildKey(string runId, string artifactName) => $"{Sanitize(runId)}/{Sanitize(artifactName)}";

    private static string Sanitize(string input)
    {
        var safe = input.Trim().Replace("\\", "/").Replace("../", "_").Replace("..", "_");
        if (string.IsNullOrWhiteSpace(safe))
        {
            throw new ArgumentException("Value cannot be empty after sanitization.", nameof(input));
        }

        return safe;
    }
}

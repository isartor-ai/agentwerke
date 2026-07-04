namespace Agentwerke.Storage.Artifacts;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "filesystem";

    public string RootPath { get; set; } = "./storage";

    public StorageS3Options S3 { get; set; } = new();
}

public sealed class StorageS3Options
{
    public string BucketName { get; set; } = string.Empty;

    public string Region { get; set; } = "us-east-1";

    public string ServiceUrl { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public bool ForcePathStyle { get; set; } = true;
}

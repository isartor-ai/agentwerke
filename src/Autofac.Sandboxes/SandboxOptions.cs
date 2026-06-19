namespace Autofac.Sandboxes;

public sealed class SandboxOptions
{
    public const string Section = "Sandboxes";

    /// <summary>
    /// Global opt-in for sandbox execution. Legacy provider-level flags remain supported
    /// during the provider-neutral transition.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Configured provider name. Supported values: docker, opensandbox, kubernetes-kata.
    /// </summary>
    public string Provider { get; set; } = SandboxProviderNames.Docker;

    public DockerSandboxProviderOptions Docker { get; set; } = new();

    public OpenSandboxProviderOptions OpenSandbox { get; set; } = new();

    public KubernetesKataSandboxProviderOptions KubernetesKata { get; set; } = new();

    /// <summary>
    /// Transitional compatibility shim so existing call sites keep working while Docker
    /// settings move under <see cref="Docker"/>.
    /// </summary>
    public string DefaultImage
    {
        get => Docker.DefaultImage;
        set => Docker.DefaultImage = value;
    }

    public int TimeoutSeconds
    {
        get => Docker.TimeoutSeconds;
        set => Docker.TimeoutSeconds = value;
    }

    public int MemoryLimitMb
    {
        get => Docker.MemoryLimitMb;
        set => Docker.MemoryLimitMb = value;
    }

    public long CpuQuota
    {
        get => Docker.CpuQuota;
        set => Docker.CpuQuota = value;
    }

    public string ArtifactsHostPath
    {
        get => Docker.ArtifactsHostPath;
        set => Docker.ArtifactsHostPath = value;
    }

    public string DockerEndpoint
    {
        get => Docker.DockerEndpoint;
        set => Docker.DockerEndpoint = value;
    }

    public bool IsEnabled => Enabled || Docker.Enabled || OpenSandbox.Enabled || KubernetesKata.Enabled;
}

public sealed class DockerSandboxProviderOptions
{
    /// <summary>Legacy provider-local enable flag retained for backward compatibility.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Docker image used when no per-task image is specified.</summary>
    public string DefaultImage { get; set; } = "alpine:3.19";

    /// <summary>Seconds before the container is killed and the run is marked timed-out.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Memory limit in megabytes applied to the container.</summary>
    public int MemoryLimitMb { get; set; } = 256;

    /// <summary>
    /// CPU quota in microseconds per 100 ms period (e.g. 50000 = 50% of one core).
    /// Passed directly to Docker's --cpu-quota.
    /// </summary>
    public long CpuQuota { get; set; } = 50_000;

    /// <summary>
    /// Host directory mounted into the container at /output so artifacts can be collected.
    /// When empty, a temp directory is created per run.
    /// </summary>
    public string ArtifactsHostPath { get; set; } = string.Empty;

    /// <summary>Docker daemon socket URI. Defaults to the local socket.</summary>
    public string DockerEndpoint { get; set; } = "unix:///var/run/docker.sock";
}

public sealed class OpenSandboxProviderOptions
{
    public bool Enabled { get; set; } = false;

    public string ServerUrl { get; set; } = "http://localhost:8080/v1";

    /// <summary>
    /// Backward-compatible alias for <see cref="ServerUrl"/>.
    /// </summary>
    public string BaseUrl
    {
        get => ServerUrl;
        set => ServerUrl = value;
    }

    public string ApiKey { get; set; } = string.Empty;

    public string DefaultImage { get; set; } = "alpine:3.19";

    public int DefaultTimeoutSeconds { get; set; } = 60;

    public int ReadinessTimeoutSeconds { get; set; } = 30;

    public int DefaultMemoryLimitMb { get; set; } = 256;

    public int DefaultCpuMilliCores { get; set; } = 500;

    public bool UseServerProxy { get; set; } = false;

    public string WorkingDirectory { get; set; } = "/workspace";

    public List<string> DefaultArtifactPaths { get; set; } = ["/output"];

    public SandboxExecutionProfile DefaultProfile { get; set; } = new();
}

public sealed class KubernetesKataSandboxProviderOptions
{
    public bool Enabled { get; set; } = false;

    public string RuntimeClassName { get; set; } = "kata";

    public string Namespace { get; set; } = "default";

    public string DefaultImage { get; set; } = "alpine:3.19";
}

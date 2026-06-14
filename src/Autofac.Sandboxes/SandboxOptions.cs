namespace Autofac.Sandboxes;

public sealed class SandboxOptions
{
    public const string Section = "Sandboxes:Docker";

    /// <summary>When false, sandbox execution is skipped and falls back to simulated output.</summary>
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

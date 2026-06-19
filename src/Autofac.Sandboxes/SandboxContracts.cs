namespace Autofac.Sandboxes;

/// <summary>
/// All inputs needed to run one agent task inside a sandbox container.
/// </summary>
public sealed record SandboxExecutionRequest(
    string RunId,
    string StepId,
    string AgentName,
    string Action,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    int Attempt,
    /// <summary>Docker image to run. Falls back to <see cref="SandboxOptions.DefaultImage"/> when null.</summary>
    string? Image = null,
    SandboxExecutionProfile? Profile = null,
    SandboxCommandSpec? Command = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    IReadOnlyList<SandboxEndpointRequest>? EndpointRequests = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    IReadOnlyList<string>? ArtifactPaths = null);

/// <summary>
/// Outcome produced after the container exits (or is timed out / fails to start).
/// </summary>
public sealed record SandboxExecutionResult(
    bool Succeeded,
    string Logs,
    string? FailureReason,
    /// <summary>
    /// Files written to /output inside the container, keyed by file name.
    /// Empty when the container produced no output files.
    /// </summary>
    IReadOnlyDictionary<string, string> Artifacts,
    int? ExitCode,
    TimeSpan Duration,
    string? ProviderSandboxId = null,
    SandboxCommandState CommandState = SandboxCommandState.Unknown,
    IReadOnlyList<SandboxLogEntry>? StructuredLogs = null,
    IReadOnlyDictionary<string, string>? ProviderDiagnostics = null,
    IReadOnlyList<SandboxEndpointMetadata>? Endpoints = null,
    SandboxProviderKind Provider = SandboxProviderKind.Docker);

public sealed record SandboxExecutionProfile(
    SandboxResourceLimits? Resources = null,
    SandboxNetworkPolicy? NetworkPolicy = null,
    IReadOnlyList<SandboxFilesystemMount>? FilesystemMounts = null,
    IReadOnlyList<SandboxCredentialBinding>? CredentialBindings = null,
    SandboxCleanupPolicy? CleanupPolicy = null,
    SandboxCommandExecutionMode CommandExecutionMode = SandboxCommandExecutionMode.Foreground);

public sealed record SandboxResourceLimits(
    int? CpuMilliCores = null,
    int? MemoryMb = null,
    int? TimeoutSeconds = null,
    int? GpuCount = null);

public sealed record SandboxNetworkPolicy(
    SandboxNetworkAccessMode Mode = SandboxNetworkAccessMode.None,
    IReadOnlyList<string>? AllowedHosts = null);

public sealed record SandboxFilesystemMount(
    SandboxFilesystemMountSourceKind SourceKind,
    string Source,
    string MountPath,
    bool ReadOnly = true);

public sealed record SandboxCredentialBinding(
    string Name,
    string Target,
    SandboxCredentialBindingMode Mode = SandboxCredentialBindingMode.File,
    bool ReadOnly = true);

public sealed record SandboxCleanupPolicy(
    bool DeleteSandboxOnCompletion = true,
    bool RetainSandboxOnFailure = false,
    bool CaptureDiagnosticsOnFailure = true);

public sealed record SandboxCommandSpec(
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    string? StandardInput = null,
    bool StreamOutput = true);

public sealed record SandboxEndpointRequest(
    int Port,
    string? Name = null,
    bool SecureAccess = false);

public sealed record SandboxEndpointMetadata(
    int Port,
    string Uri,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Headers = null);

public sealed record SandboxLogEntry(
    string Stream,
    string Message,
    DateTimeOffset Timestamp);

public enum SandboxCommandExecutionMode
{
    Foreground,
    Background,
    Session
}

public enum SandboxCommandState
{
    Unknown,
    Running,
    Completed,
    Failed,
    TimedOut,
    Cancelled
}

public enum SandboxNetworkAccessMode
{
    None,
    Restricted,
    Open
}

public enum SandboxFilesystemMountSourceKind
{
    HostPath,
    NamedVolume,
    PersistentVolumeClaim,
    Ossfs
}

public enum SandboxCredentialBindingMode
{
    File,
    EnvironmentVariable
}

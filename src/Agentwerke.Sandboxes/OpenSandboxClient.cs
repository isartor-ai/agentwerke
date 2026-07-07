namespace Agentwerke.Sandboxes;

public interface IOpenSandboxClient
{
    Task<OpenSandboxSandboxHandle> CreateAsync(
        OpenSandboxCreateSandboxRequest request,
        CancellationToken cancellationToken);

    Task<OpenSandboxCommandResult> RunCommandAsync(
        string sandboxId,
        OpenSandboxRunCommandRequest request,
        CancellationToken cancellationToken,
        SandboxLogReporter? logReporter = null);

    Task<IReadOnlyList<OpenSandboxArtifactFile>> CollectArtifactsAsync(
        string sandboxId,
        OpenSandboxCollectArtifactsRequest request,
        CancellationToken cancellationToken);

    Task<OpenSandboxEndpointResult> ResolveEndpointAsync(
        string sandboxId,
        OpenSandboxResolveEndpointRequest request,
        CancellationToken cancellationToken);

    Task<OpenSandboxDiagnosticsResult> GetDiagnosticsAsync(
        string sandboxId,
        CancellationToken cancellationToken);

    Task InterruptCommandAsync(
        string sandboxId,
        OpenSandboxInterruptCommandRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string sandboxId,
        CancellationToken cancellationToken);
}

public sealed class UnimplementedOpenSandboxClient : IOpenSandboxClient
{
    private static InvalidOperationException NotImplemented() =>
        new("OpenSandbox client implementation is not registered yet. Issue #126 adds the runtime-backed provider.");

    public Task<OpenSandboxSandboxHandle> CreateAsync(
        OpenSandboxCreateSandboxRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<OpenSandboxSandboxHandle>(NotImplemented());

    public Task<OpenSandboxCommandResult> RunCommandAsync(
        string sandboxId,
        OpenSandboxRunCommandRequest request,
        CancellationToken cancellationToken,
        SandboxLogReporter? logReporter = null) =>
        Task.FromException<OpenSandboxCommandResult>(NotImplemented());

    public Task<IReadOnlyList<OpenSandboxArtifactFile>> CollectArtifactsAsync(
        string sandboxId,
        OpenSandboxCollectArtifactsRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<OpenSandboxArtifactFile>>(NotImplemented());

    public Task<OpenSandboxEndpointResult> ResolveEndpointAsync(
        string sandboxId,
        OpenSandboxResolveEndpointRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<OpenSandboxEndpointResult>(NotImplemented());

    public Task<OpenSandboxDiagnosticsResult> GetDiagnosticsAsync(
        string sandboxId,
        CancellationToken cancellationToken) =>
        Task.FromException<OpenSandboxDiagnosticsResult>(NotImplemented());

    public Task InterruptCommandAsync(
        string sandboxId,
        OpenSandboxInterruptCommandRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException(NotImplemented());

    public Task DeleteAsync(
        string sandboxId,
        CancellationToken cancellationToken) =>
        Task.FromException(NotImplemented());
}

public sealed record OpenSandboxCreateSandboxRequest(
    string Image,
    int TimeoutSeconds,
    OpenSandboxResourceLimits ResourceLimits,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<OpenSandboxVolumeMount> Volumes,
    OpenSandboxNetworkPolicy? NetworkPolicy,
    IReadOnlyList<OpenSandboxCredentialBinding> CredentialBindings,
    IReadOnlyList<OpenSandboxResolveEndpointRequest> RequestedEndpoints,
    bool SecureAccess,
    string? WorkingDirectory,
    SandboxCommandExecutionMode CommandExecutionMode);

public sealed record OpenSandboxRunCommandRequest(
    IReadOnlyList<string> Arguments,
    SandboxCommandExecutionMode Mode,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    int TimeoutSeconds,
    string? StandardInput,
    bool StreamOutput);

public sealed record OpenSandboxCollectArtifactsRequest(
    IReadOnlyList<string> Paths);

public sealed record OpenSandboxInterruptCommandRequest(
    string CommandId);

public sealed record OpenSandboxResolveEndpointRequest(
    int Port,
    string? Name = null,
    bool SecureAccess = false);

public sealed record OpenSandboxSandboxHandle(
    string SandboxId,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record OpenSandboxCommandResult(
    SandboxCommandState State,
    int? ExitCode,
    string Logs,
    IReadOnlyList<SandboxLogEntry> StructuredLogs,
    string? ExecutionId = null,
    string? SessionId = null,
    string? FailureReason = null,
    IReadOnlyDictionary<string, string>? Diagnostics = null);

public sealed record OpenSandboxArtifactFile(
    string Path,
    string Content);

public sealed record OpenSandboxEndpointResult(
    int Port,
    string Uri,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Headers = null);

public sealed record OpenSandboxDiagnosticsResult(
    IReadOnlyDictionary<string, string> Entries);

public sealed record OpenSandboxResourceLimits(
    int? CpuMilliCores,
    int? MemoryMb,
    int? TimeoutSeconds,
    int? GpuCount);

public sealed record OpenSandboxVolumeMount(
    SandboxFilesystemMountSourceKind SourceKind,
    string Source,
    string MountPath,
    bool ReadOnly);

public sealed record OpenSandboxNetworkPolicy(
    SandboxNetworkAccessMode Mode,
    IReadOnlyList<string> AllowedHosts);

public sealed record OpenSandboxCredentialBinding(
    string Name,
    string Target,
    SandboxCredentialBindingMode Mode,
    bool ReadOnly);

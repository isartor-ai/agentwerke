namespace Agentwerke.Sandboxes;

/// <summary>
/// Canonical names for the sandbox profiles Autofac exposes to agents and workflows.
/// </summary>
public static class SandboxProfileNames
{
    public const string Offline = "offline";
    public const string RepoRead = "repo-read";
    public const string RepoWrite = "repo-write";
    public const string Deployment = "deployment";

    public static readonly IReadOnlyList<string> All = [Offline, RepoRead, RepoWrite, Deployment];
}

/// <summary>
/// A named sandbox profile: a deterministic <see cref="SandboxExecutionProfile"/> plus the
/// credential names it expects the secret/vault layer to bind.
/// </summary>
public sealed record SandboxProfileDefinition(
    string Name,
    string Description,
    SandboxExecutionProfile Profile,
    IReadOnlyList<string> RequiredCredentials);

/// <summary>
/// Maps named Autofac sandbox profiles (<see cref="SandboxProfileNames"/>) onto concrete,
/// OpenSandbox-facing <see cref="SandboxExecutionProfile"/> definitions.
///
/// Definitions describe the *shape* of access (resource limits, egress, mounts, credential
/// bindings); run-scoped identifiers such as workspace volume names are filled in by
/// <see cref="Resolve"/> so concurrent runs never share a mount. Credential bindings resolve
/// by a fixed name from the secret/vault layer and are never inlined as command arguments.
/// </summary>
public static class SandboxProfileCatalog
{
    private static readonly IReadOnlyList<string> RepoHosts =
    [
        "github.com",
        "api.github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com"
    ];

    private static readonly Dictionary<string, SandboxProfileDefinition> Definitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SandboxProfileNames.Offline] = new SandboxProfileDefinition(
                Name: SandboxProfileNames.Offline,
                Description: "No network egress, no repository mount, no credentials. Default for analysis, " +
                    "planning, and spec-generation agents that only read their prompt and produce text output.",
                Profile: new SandboxExecutionProfile(
                    Resources: new SandboxResourceLimits(CpuMilliCores: 250, MemoryMb: 256, TimeoutSeconds: 60),
                    NetworkPolicy: new SandboxNetworkPolicy(SandboxNetworkAccessMode.None),
                    FilesystemMounts: [],
                    CredentialBindings: [],
                    CleanupPolicy: new SandboxCleanupPolicy()),
                RequiredCredentials: []),

            [SandboxProfileNames.RepoRead] = new SandboxProfileDefinition(
                Name: SandboxProfileNames.RepoRead,
                Description: "Restricted egress to source-control hosts with a read-only repository checkout and " +
                    "a file-mounted, read-only GitHub token. For review and analysis agents that need repository " +
                    "context but must not write.",
                Profile: new SandboxExecutionProfile(
                    Resources: new SandboxResourceLimits(CpuMilliCores: 500, MemoryMb: 512, TimeoutSeconds: 120),
                    NetworkPolicy: new SandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, RepoHosts),
                    FilesystemMounts:
                    [
                        new SandboxFilesystemMount(SandboxFilesystemMountSourceKind.NamedVolume, "workspace", "/workspace", ReadOnly: true)
                    ],
                    CredentialBindings:
                    [
                        new SandboxCredentialBinding("github-token", "/var/run/secrets/github/token", SandboxCredentialBindingMode.File, ReadOnly: true)
                    ],
                    CleanupPolicy: new SandboxCleanupPolicy()),
                RequiredCredentials: ["github-token"]),

            [SandboxProfileNames.RepoWrite] = new SandboxProfileDefinition(
                Name: SandboxProfileNames.RepoWrite,
                Description: "Restricted egress to source-control hosts with a writable repository checkout and a " +
                    "file-mounted GitHub token. For implementation agents that commit changes and open pull requests.",
                Profile: new SandboxExecutionProfile(
                    Resources: new SandboxResourceLimits(CpuMilliCores: 1_000, MemoryMb: 1_024, TimeoutSeconds: 600),
                    NetworkPolicy: new SandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, RepoHosts),
                    FilesystemMounts:
                    [
                        new SandboxFilesystemMount(SandboxFilesystemMountSourceKind.NamedVolume, "workspace", "/workspace", ReadOnly: false)
                    ],
                    CredentialBindings:
                    [
                        new SandboxCredentialBinding("github-token", "/var/run/secrets/github/token", SandboxCredentialBindingMode.File, ReadOnly: true)
                    ],
                    CleanupPolicy: new SandboxCleanupPolicy(RetainSandboxOnFailure: true, CaptureDiagnosticsOnFailure: true)),
                RequiredCredentials: ["github-token"]),

            [SandboxProfileNames.Deployment] = new SandboxProfileDefinition(
                Name: SandboxProfileNames.Deployment,
                Description: "No source checkout. Restricted, explicitly allow-listed egress to deployment-target " +
                    "endpoints with a file-mounted deployment credential. For deploy, rollback, and promote agents.",
                Profile: new SandboxExecutionProfile(
                    Resources: new SandboxResourceLimits(CpuMilliCores: 1_000, MemoryMb: 1_024, TimeoutSeconds: 900),
                    NetworkPolicy: new SandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, []),
                    FilesystemMounts: [],
                    CredentialBindings:
                    [
                        new SandboxCredentialBinding("deployment-credentials", "/var/run/secrets/deployment/credentials.json", SandboxCredentialBindingMode.File, ReadOnly: true)
                    ],
                    CleanupPolicy: new SandboxCleanupPolicy(RetainSandboxOnFailure: true, CaptureDiagnosticsOnFailure: true)),
                RequiredCredentials: ["deployment-credentials"])
        };

    public static string Default => SandboxProfileNames.Offline;

    public static IReadOnlyList<string> Names => SandboxProfileNames.All;

    public static IReadOnlyList<SandboxProfileDefinition> All => Definitions.Values.ToList();

    public static bool TryGet(string name, out SandboxProfileDefinition? definition) =>
        Definitions.TryGetValue(name, out definition);

    /// <summary>
    /// Resolves a named profile into a run-scoped <see cref="SandboxExecutionProfile"/>.
    /// Workspace volume names are namespaced by <paramref name="runId"/> so concurrent runs
    /// never share a mount. Throws if the profile name is not in the catalog; callers should
    /// reject unknown profiles before reaching this point so the failure carries a richer
    /// diagnostic than this exception.
    /// </summary>
    public static SandboxExecutionProfile Resolve(string profileName, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (!TryGet(profileName, out var definition) || definition is null)
        {
            throw new InvalidOperationException(
                $"Unknown sandbox profile '{profileName}'. Known profiles: {string.Join(", ", Names)}.");
        }

        var profile = definition.Profile;
        var mounts = profile.FilesystemMounts?
            .Select(mount => mount with { Source = $"autofac-run-{runId}-{mount.Source}" })
            .ToArray();

        return profile with { FilesystemMounts = mounts };
    }
}

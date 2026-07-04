using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Agentwerke.Sandboxes;

public sealed class OpenSandboxRequestMapper
{
    private readonly SandboxOptions _options;

    public OpenSandboxRequestMapper(IOptions<SandboxOptions> options)
    {
        _options = options.Value;
    }

    public OpenSandboxCreateSandboxRequest MapCreateRequest(SandboxExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var providerOptions = _options.OpenSandbox;
        var profile = GetEffectiveProfile(request);
        var resources = profile.Resources ?? new SandboxResourceLimits();
        var environment = BuildEnvironment(request);
        var metadata = BuildMetadata(request);

        return new OpenSandboxCreateSandboxRequest(
            Image: request.Image ?? providerOptions.DefaultImage,
            TimeoutSeconds: resources.TimeoutSeconds ?? providerOptions.DefaultTimeoutSeconds,
            ResourceLimits: new OpenSandboxResourceLimits(
                CpuMilliCores: resources.CpuMilliCores ?? providerOptions.DefaultCpuMilliCores,
                MemoryMb: resources.MemoryMb ?? providerOptions.DefaultMemoryLimitMb,
                TimeoutSeconds: resources.TimeoutSeconds ?? providerOptions.DefaultTimeoutSeconds,
                GpuCount: resources.GpuCount),
            EnvironmentVariables: environment,
            Metadata: metadata,
            Volumes: profile.FilesystemMounts?.Select(mount => new OpenSandboxVolumeMount(
                mount.SourceKind,
                mount.Source,
                mount.MountPath,
                mount.ReadOnly)).ToArray() ?? [],
            // Omit networkPolicy entirely for "None" rather than sending an explicit
            // no-egress policy: OpenSandbox treats the field's mere presence (any mode,
            // including "none") as "enforce a policy" and spins up an egress sidecar for
            // it, regardless of mode. "None" needs no enforcement, so there is nothing
            // to gain from asking for it and a real cost — the sidecar adds ~30s of
            // latency per sandbox and, depending on the server's [egress] mode, may not
            // even become ready (see docs/manual-test-opensandbox.md).
            NetworkPolicy: profile.NetworkPolicy is null or { Mode: SandboxNetworkAccessMode.None }
                ? null
                : new OpenSandboxNetworkPolicy(
                    profile.NetworkPolicy.Mode,
                    profile.NetworkPolicy.AllowedHosts ?? []),
            CredentialBindings: profile.CredentialBindings?.Select(binding => new OpenSandboxCredentialBinding(
                binding.Name,
                binding.Target,
                binding.Mode,
                binding.ReadOnly)).ToArray() ?? [],
            RequestedEndpoints: request.EndpointRequests?.Select(endpoint => new OpenSandboxResolveEndpointRequest(
                endpoint.Port,
                endpoint.Name,
                endpoint.SecureAccess)).ToArray() ?? [],
            SecureAccess: request.EndpointRequests?.Any(static endpoint => endpoint.SecureAccess) == true,
            WorkingDirectory: request.Command?.WorkingDirectory ?? providerOptions.WorkingDirectory,
            CommandExecutionMode: profile.CommandExecutionMode);
    }

    public OpenSandboxRunCommandRequest MapRunCommandRequest(SandboxExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = GetEffectiveProfile(request);
        var command = SandboxRequestDefaults.ResolveCommand(request);
        var environment = BuildCommandEnvironment(request, command);
        var timeoutSeconds = profile.Resources?.TimeoutSeconds ?? _options.OpenSandbox.DefaultTimeoutSeconds;

        return new OpenSandboxRunCommandRequest(
            Arguments: command.Arguments,
            Mode: profile.CommandExecutionMode,
            WorkingDirectory: command.WorkingDirectory ?? _options.OpenSandbox.WorkingDirectory,
            EnvironmentVariables: environment,
            TimeoutSeconds: timeoutSeconds,
            StandardInput: command.StandardInput,
            StreamOutput: command.StreamOutput);
    }

    public OpenSandboxCollectArtifactsRequest MapCollectArtifactsRequest(SandboxExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new OpenSandboxCollectArtifactsRequest(
            SandboxRequestDefaults.ResolveArtifactPaths(request, _options.OpenSandbox.DefaultArtifactPaths));
    }

    public OpenSandboxInterruptCommandRequest MapInterruptCommandRequest(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return new OpenSandboxInterruptCommandRequest(sessionId);
    }

    private SandboxExecutionProfile GetEffectiveProfile(SandboxExecutionRequest request)
    {
        var defaults = _options.OpenSandbox.DefaultProfile ?? new SandboxExecutionProfile();
        var profile = request.Profile;

        return new SandboxExecutionProfile(
            Resources: MergeResources(defaults.Resources, profile?.Resources),
            NetworkPolicy: profile?.NetworkPolicy ?? defaults.NetworkPolicy,
            FilesystemMounts: profile?.FilesystemMounts ?? defaults.FilesystemMounts,
            CredentialBindings: profile?.CredentialBindings ?? defaults.CredentialBindings,
            CleanupPolicy: profile?.CleanupPolicy ?? defaults.CleanupPolicy,
            CommandExecutionMode: profile?.CommandExecutionMode ?? defaults.CommandExecutionMode);
    }

    private static SandboxResourceLimits? MergeResources(
        SandboxResourceLimits? defaults,
        SandboxResourceLimits? requested)
    {
        if (defaults is null && requested is null)
        {
            return null;
        }

        return new SandboxResourceLimits(
            CpuMilliCores: requested?.CpuMilliCores ?? defaults?.CpuMilliCores,
            MemoryMb: requested?.MemoryMb ?? defaults?.MemoryMb,
            TimeoutSeconds: requested?.TimeoutSeconds ?? defaults?.TimeoutSeconds,
            GpuCount: requested?.GpuCount ?? defaults?.GpuCount);
    }

    private static IReadOnlyDictionary<string, string> BuildEnvironment(SandboxExecutionRequest request)
    {
        var environment = SandboxRequestDefaults.BuildExecutionEnvironment(request);
        foreach (var pair in request.EnvironmentVariables?.AsEnumerable() ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            environment[pair.Key] = pair.Value;
        }

        return environment;
    }

    private static IReadOnlyDictionary<string, string> BuildCommandEnvironment(
        SandboxExecutionRequest request,
        SandboxCommandSpec command)
    {
        var environment = BuildEnvironment(request).ToDictionary(static pair => pair.Key, static pair => pair.Value);
        foreach (var pair in command.EnvironmentVariables?.AsEnumerable() ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            environment[pair.Key] = pair.Value;
        }

        return environment;
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(SandboxExecutionRequest request)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["agentwerke.run"] = request.RunId,
            ["agentwerke.step"] = request.StepId,
            ["agentwerke.agent"] = request.AgentName,
            ["agentwerke.action"] = request.Action,
            ["agentwerke.purpose"] = request.PurposeType,
            ["agentwerke.policy"] = request.PolicyTag,
            ["agentwerke.attempt"] = request.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(request.Environment))
        {
            metadata["agentwerke.environment"] = request.Environment;
        }

        foreach (var pair in request.Metadata?.AsEnumerable() ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            metadata[pair.Key] = pair.Value;
        }

        return metadata.ToDictionary(
            static pair => pair.Key,
            static pair => SanitizeMetadataValue(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string SanitizeMetadataValue(string value)
    {
        var sanitized = new string(value
            .Select(static ch => IsMetadataValueCharacter(ch) ? ch : '-')
            .ToArray())
            .Trim('-', '_', '.');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "empty";
        }

        if (sanitized.Length <= 63)
        {
            return sanitized;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..8];
        var prefixLength = 63 - hash.Length - 1;
        var prefix = sanitized[..Math.Min(prefixLength, sanitized.Length)].TrimEnd('-', '_', '.');

        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "value";
        }

        return $"{prefix}-{hash}";
    }

    private static bool IsMetadataValueCharacter(char ch) =>
        (ch >= 'a' && ch <= 'z') ||
        (ch >= 'A' && ch <= 'Z') ||
        (ch >= '0' && ch <= '9') ||
        ch is '-' or '_' or '.';
}

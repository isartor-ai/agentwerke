using Microsoft.Extensions.Options;

namespace Autofac.Sandboxes;

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

        var profile = request.Profile ?? new SandboxExecutionProfile();
        var resources = profile.Resources ?? new SandboxResourceLimits();
        var providerOptions = _options.OpenSandbox;
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
            NetworkPolicy: profile.NetworkPolicy is null
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
            WorkingDirectory: request.Command?.WorkingDirectory ?? providerOptions.WorkingDirectory,
            CommandExecutionMode: profile.CommandExecutionMode);
    }

    public OpenSandboxRunCommandRequest MapRunCommandRequest(SandboxExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = request.Profile ?? new SandboxExecutionProfile();
        var command = SandboxRequestDefaults.ResolveCommand(request);
        var environment = BuildCommandEnvironment(request, command);

        return new OpenSandboxRunCommandRequest(
            Arguments: command.Arguments,
            Mode: profile.CommandExecutionMode,
            WorkingDirectory: command.WorkingDirectory ?? _options.OpenSandbox.WorkingDirectory,
            EnvironmentVariables: environment,
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
            ["autofac.run"] = request.RunId,
            ["autofac.step"] = request.StepId,
            ["autofac.agent"] = request.AgentName,
            ["autofac.action"] = request.Action,
            ["autofac.purpose"] = request.PurposeType,
            ["autofac.policy"] = request.PolicyTag,
            ["autofac.attempt"] = request.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(request.Environment))
        {
            metadata["autofac.environment"] = request.Environment;
        }

        foreach (var pair in request.Metadata?.AsEnumerable() ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            metadata[pair.Key] = pair.Value;
        }

        return metadata;
    }
}

using Microsoft.Extensions.Options;

namespace Autofac.Sandboxes;

/// <summary>
/// Routes each sandbox run to a provider executor. The globally configured provider
/// (<see cref="ActiveProvider"/>) is the default, but a run whose profile pins a
/// provider (<see cref="SandboxExecutionProfile.Provider"/>) is routed there instead —
/// so the execution backend can be selected per policy/project (#36).
/// </summary>
public sealed class ConfiguredSandboxExecutor : ISandboxExecutor
{
    private readonly IReadOnlyDictionary<SandboxProviderKind, ISandboxProviderExecutor> _providers;

    public ConfiguredSandboxExecutor(
        IEnumerable<ISandboxProviderExecutor> providers,
        IOptions<SandboxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);

        _providers = providers.ToDictionary(provider => provider.ProviderKind);
        ActiveProvider = SandboxProviderNames.Parse(options.Value.Provider);

        if (!_providers.ContainsKey(ActiveProvider))
        {
            throw new InvalidOperationException(
                $"Sandbox provider '{ActiveProvider.ToConfigValue()}' is configured, but no matching executor is registered.");
        }
    }

    /// <summary>The default provider used when a run's profile does not pin one.</summary>
    public SandboxProviderKind ActiveProvider { get; }

    public Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var providerKind = request.Profile?.Provider ?? ActiveProvider;

        if (!_providers.TryGetValue(providerKind, out var executor))
        {
            throw new InvalidOperationException(
                $"Sandbox profile requested provider '{providerKind.ToConfigValue()}', but no matching executor is registered. " +
                $"Registered providers: {string.Join(", ", _providers.Keys.Select(kind => kind.ToConfigValue()))}.");
        }

        return executor.ExecuteAsync(request, cancellationToken);
    }
}

using Microsoft.Extensions.Options;

namespace Autofac.Sandboxes;

public sealed class ConfiguredSandboxExecutor : ISandboxExecutor
{
    private readonly ISandboxProviderExecutor _inner;

    public ConfiguredSandboxExecutor(
        IEnumerable<ISandboxProviderExecutor> providers,
        IOptions<SandboxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);

        var providerMap = providers.ToDictionary(provider => provider.ProviderKind);
        ActiveProvider = SandboxProviderNames.Parse(options.Value.Provider);

        if (!providerMap.TryGetValue(ActiveProvider, out _inner!))
        {
            throw new InvalidOperationException(
                $"Sandbox provider '{ActiveProvider.ToConfigValue()}' is configured, but no matching executor is registered.");
        }
    }

    public SandboxProviderKind ActiveProvider { get; }

    public Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken) =>
        _inner.ExecuteAsync(request, cancellationToken);
}

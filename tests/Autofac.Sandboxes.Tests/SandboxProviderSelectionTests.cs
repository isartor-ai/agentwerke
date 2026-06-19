using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autofac.Sandboxes.Tests;

public sealed class SandboxProviderSelectionTests
{
    [Theory]
    [InlineData(SandboxProviderNames.Docker, SandboxProviderKind.Docker)]
    [InlineData(SandboxProviderNames.OpenSandbox, SandboxProviderKind.OpenSandbox)]
    [InlineData(SandboxProviderNames.KubernetesKata, SandboxProviderKind.KubernetesKata)]
    public async Task AddAutofacSandboxes_ResolvesConfiguredProvider(string providerName, SandboxProviderKind expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SandboxOptions.Section}:Provider"] = providerName
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddAutofacSandboxes(configuration);

        await using var serviceProvider = services.BuildServiceProvider();
        var executor = serviceProvider.GetRequiredService<ConfiguredSandboxExecutor>();

        Assert.Equal(expected, executor.ActiveProvider);
        Assert.Same(executor, serviceProvider.GetRequiredService<ISandboxExecutor>());
    }

    [Fact]
    public async Task AddAutofacSandboxes_UnsupportedProvider_FailsWithActionableError()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SandboxOptions.Section}:Provider"] = "temporal"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddAutofacSandboxes(configuration);

        await using var serviceProvider = services.BuildServiceProvider();
        var exception = Assert.Throws<InvalidOperationException>(
            () => serviceProvider.GetRequiredService<ConfiguredSandboxExecutor>());

        Assert.Contains("temporal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{SandboxOptions.Section}:Provider", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

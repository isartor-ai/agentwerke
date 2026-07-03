using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentwerke.Sandboxes.Tests;

public sealed class SandboxProviderSelectionTests
{
    [Theory]
    [InlineData(SandboxProviderNames.Docker, SandboxProviderKind.Docker)]
    [InlineData(SandboxProviderNames.OpenSandbox, SandboxProviderKind.OpenSandbox)]
    [InlineData(SandboxProviderNames.KubernetesKata, SandboxProviderKind.KubernetesKata)]
    public async Task AddAgentwerkeSandboxes_ResolvesConfiguredProvider(string providerName, SandboxProviderKind expected)
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
        services.AddAgentwerkeSandboxes(configuration);

        await using var serviceProvider = services.BuildServiceProvider();
        var executor = serviceProvider.GetRequiredService<ConfiguredSandboxExecutor>();

        Assert.Equal(expected, executor.ActiveProvider);
        Assert.Same(executor, serviceProvider.GetRequiredService<ISandboxExecutor>());
    }

    [Fact]
    public async Task AddAgentwerkeSandboxes_UnsupportedProvider_FailsWithActionableError()
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
        services.AddAgentwerkeSandboxes(configuration);

        await using var serviceProvider = services.BuildServiceProvider();
        var exception = Assert.Throws<InvalidOperationException>(
            () => serviceProvider.GetRequiredService<ConfiguredSandboxExecutor>());

        Assert.Contains("temporal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{SandboxOptions.Section}:Provider", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_RoutesToProfilePinnedProvider()
    {
        var executor = new ConfiguredSandboxExecutor(
            [new FakeProviderExecutor(SandboxProviderKind.Docker), new FakeProviderExecutor(SandboxProviderKind.KubernetesKata)],
            Options.Create(new SandboxOptions { Provider = SandboxProviderNames.Docker }));

        var result = await executor.ExecuteAsync(Request(SandboxProviderKind.KubernetesKata), CancellationToken.None);

        Assert.Equal(SandboxProviderKind.KubernetesKata, result.Provider);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToConfiguredDefault_WhenProfileHasNoProvider()
    {
        var executor = new ConfiguredSandboxExecutor(
            [new FakeProviderExecutor(SandboxProviderKind.Docker), new FakeProviderExecutor(SandboxProviderKind.KubernetesKata)],
            Options.Create(new SandboxOptions { Provider = SandboxProviderNames.KubernetesKata }));

        var result = await executor.ExecuteAsync(Request(null), CancellationToken.None);

        Assert.Equal(SandboxProviderKind.KubernetesKata, result.Provider);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPinnedProviderNotRegistered_ThrowsActionableError()
    {
        var executor = new ConfiguredSandboxExecutor(
            [new FakeProviderExecutor(SandboxProviderKind.Docker)],
            Options.Create(new SandboxOptions { Provider = SandboxProviderNames.Docker }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(Request(SandboxProviderKind.KubernetesKata), CancellationToken.None));

        Assert.Contains(SandboxProviderNames.KubernetesKata, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SandboxExecutionRequest Request(SandboxProviderKind? provider) =>
        new(
            RunId: "run",
            StepId: "step",
            AgentName: "agent",
            Action: "action",
            Environment: null,
            PurposeType: "general",
            PolicyTag: "tag",
            Attempt: 1,
            Profile: provider is null ? null : new SandboxExecutionProfile(Provider: provider));

    private sealed class FakeProviderExecutor : ISandboxProviderExecutor
    {
        public FakeProviderExecutor(SandboxProviderKind kind) => ProviderKind = kind;

        public SandboxProviderKind ProviderKind { get; }

        public Task<SandboxExecutionResult> ExecuteAsync(
            SandboxExecutionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SandboxExecutionResult(
                Succeeded: true,
                Logs: string.Empty,
                FailureReason: null,
                Artifacts: new Dictionary<string, string>(),
                ExitCode: 0,
                Duration: TimeSpan.Zero,
                Provider: ProviderKind));
    }
}

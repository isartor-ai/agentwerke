using Autofac.AgentSecOps;
using Autofac.Api.Controllers;
using Autofac.Infrastructure;
using Autofac.Infrastructure.Policies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autofac.Api.Tests;

public sealed class WorkflowRuntimeModeTests
{
    [Fact]
    public void Resolve_WithoutConfiguration_DefaultsToAgentwerke()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = WorkflowRuntimeOptions.Resolve(configuration);

        Assert.Equal(WorkflowRuntimeMode.Agentwerke, options.Mode);
        Assert.False(options.IsCamundaMode);
        Assert.False(options.LegacyModeAliasUsed);
    }

    [Theory]
    [InlineData("Agentwerke")]
    [InlineData("agentwerke")]
    [InlineData(" AGENTWERKE ")]
    public void Resolve_WithAgentwerkeValue_IsCaseInsensitiveAndTrimmed(string value)
    {
        var configuration = BuildConfiguration(value);

        var options = WorkflowRuntimeOptions.Resolve(configuration);

        Assert.Equal(WorkflowRuntimeMode.Agentwerke, options.Mode);
        Assert.False(options.IsCamundaMode);
        Assert.False(options.LegacyModeAliasUsed);
    }

    [Theory]
    [InlineData("Autofac")]
    [InlineData("autofac")]
    [InlineData(" AUTOFAC ")]
    public void Resolve_WithLegacyAutofacValue_MapsToAgentwerkeRuntime(string value)
    {
        var configuration = BuildConfiguration(value);

        var options = WorkflowRuntimeOptions.Resolve(configuration);

        Assert.Equal(WorkflowRuntimeMode.Agentwerke, options.Mode);
        Assert.False(options.IsCamundaMode);
        Assert.True(options.LegacyModeAliasUsed);
    }

    [Theory]
    [InlineData("Camunda")]
    [InlineData("camunda")]
    [InlineData(" CAMUNDA ")]
    public void Resolve_WithCamundaValue_IsCaseInsensitiveAndTrimmed(string value)
    {
        var configuration = BuildConfiguration(value);

        var options = WorkflowRuntimeOptions.Resolve(configuration);

        Assert.Equal(WorkflowRuntimeMode.Camunda, options.Mode);
        Assert.True(options.IsCamundaMode);
        Assert.False(options.LegacyModeAliasUsed);
    }

    [Fact]
    public void Resolve_WithUnsupportedValue_ThrowsActionableError()
    {
        var configuration = BuildConfiguration("Temporal");

        var exception = Assert.Throws<InvalidOperationException>(
            () => WorkflowRuntimeOptions.Resolve(configuration));

        Assert.Contains("Temporal", exception.Message);
        Assert.Contains("WorkflowRuntime:Mode", exception.Message);
        Assert.Contains("Agentwerke", exception.Message);
        Assert.Contains("Autofac", exception.Message);
        Assert.Contains("Camunda", exception.Message);
    }

    [Fact]
    public void AddAutofacInfrastructure_DefaultMode_DoesNotWireCamundaClientsOrConfig()
    {
        using var provider = BuildProvider(mode: null);

        // No Camunda client or configuration is registered in the default Agentwerke runtime.
        Assert.Null(provider.GetService<ICamundaClient>());
        Assert.Null(provider.GetService<CamundaClient>());

        var runtimeOptions = provider.GetRequiredService<WorkflowRuntimeOptions>();
        Assert.Equal(WorkflowRuntimeMode.Agentwerke, runtimeOptions.Mode);

        var statusService = provider.GetRequiredService<ICamundaRuntimeStatusService>();
        Assert.IsType<DisabledCamundaRuntimeStatusService>(statusService);
    }

    [Fact]
    public async Task DefaultMode_StatusService_ReportsInactiveWithoutCallingCamunda()
    {
        using var provider = BuildProvider(mode: null);

        var statusService = provider.GetRequiredService<ICamundaRuntimeStatusService>();
        var status = await statusService.GetStatusAsync(CancellationToken.None);

        Assert.False(status.Enabled);
        Assert.False(status.Configured);
        Assert.False(status.Reachable);
        Assert.Equal(DisabledCamundaRuntimeStatusService.InactiveMessage, status.Error);
    }

    [Fact]
    public void AddAutofacInfrastructure_CamundaMode_WiresCamundaClient()
    {
        using var provider = BuildProvider(mode: "Camunda");

        Assert.NotNull(provider.GetService<ICamundaClient>());
        Assert.IsType<CamundaRuntimeStatusService>(provider.GetRequiredService<ICamundaRuntimeStatusService>());
        Assert.Equal(WorkflowRuntimeMode.Camunda, provider.GetRequiredService<WorkflowRuntimeOptions>().Mode);
    }

    [Fact]
    public void HealthController_Runtime_ReturnsActiveMode()
    {
        var controller = new HealthController(
            new DisabledCamundaRuntimeStatusService(),
            new WorkflowRuntimeOptions { Mode = WorkflowRuntimeMode.Agentwerke });

        var ok = Assert.IsType<OkObjectResult>(controller.Runtime());
        var payload = ok.Value!;
        var mode = payload.GetType().GetProperty("mode")!.GetValue(payload);
        var camundaEnabled = payload.GetType().GetProperty("camundaEnabled")!.GetValue(payload);

        Assert.Equal("Agentwerke", mode);
        Assert.Equal(false, camundaEnabled);
    }

    [Fact]
    public void AddAutofacInfrastructure_RegistersFilePolicyRuleStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=autofac;Username=test;Password=test"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAutofacInfrastructure(configuration);

        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IPolicyRuleStore));
        Assert.Equal(typeof(FilePolicyRuleStore), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    private static IConfiguration BuildConfiguration(string mode)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkflowRuntime:Mode"] = mode
            })
            .Build();
    }

    private static ServiceProvider BuildProvider(string? mode)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=autofac;Username=test;Password=test"
        };

        if (mode is not null)
        {
            settings["WorkflowRuntime:Mode"] = mode;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutofacInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}

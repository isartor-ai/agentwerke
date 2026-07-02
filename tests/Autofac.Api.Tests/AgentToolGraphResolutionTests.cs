using Autofac.Agents;
using Autofac.Agents.Tools;
using Autofac.AgentSecOps;
using Autofac.Infrastructure;
using Autofac.Integrations;
using Autofac.Observability;
using Autofac.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autofac.Api.Tests;

public sealed class AgentToolGraphResolutionTests
{
    // Regression guard for the agent.request DI cycle (#192): the tool is collected into
    // IToolRegistry, and resolving the model runner it uses would loop back through
    // ToolGateway → ToolRegistry. Resolving the tool registry must not throw.
    [Fact]
    public void ToolRegistry_ResolvesWithoutCircularDependency_AndIncludesAgentRequest()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=autofac;Username=test;Password=test",
            })
            .Build();

        // Mirror the app's service registrations that the tool graph depends on (Program.cs).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutofacObservability(configuration);
        services.AddAutofacInfrastructure(configuration);
        services.AddAutofacStorage(configuration);
        services.AddAutofacAgentSecOps();
        services.AddAutofacAgents(configuration);
        services.AddAutofacIntegrations(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IToolRegistry>();

        Assert.Contains(registry.All(), tool => tool.Name == "agent.request");
    }
}

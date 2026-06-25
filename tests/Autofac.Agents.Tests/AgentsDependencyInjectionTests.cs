using Autofac.Agents;
using Autofac.Agents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autofac.Agents.Tests;

/// <summary>
/// Guards the DI wiring that the API host depends on at startup — in particular the
/// IHttpClientFactory-based registration of the Anthropic client added for issue #143. A broken
/// registration here would surface as "API did not become healthy" in the Docker E2E suite.
/// </summary>
public sealed class AgentsDependencyInjectionTests
{
    private static IServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddAutofacAgents(config);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }

    [Fact]
    public void WithApiKey_ResolvesRealClientThroughHttpClientFactory()
    {
        using var provider = (ServiceProvider)Build(("Anthropic:ApiKey", "test-key"));
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ILanguageModelClient>();
        var retryHandler = scope.ServiceProvider.GetRequiredService<AnthropicRetryHandler>();

        Assert.IsType<AnthropicLanguageModelClient>(client);
        Assert.NotNull(retryHandler);
    }

    [Fact]
    public void WithoutApiKey_ResolvesNullClient()
    {
        using var provider = (ServiceProvider)Build();
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ILanguageModelClient>();

        Assert.IsType<NullLanguageModelClient>(client);
    }
}

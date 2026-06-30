using Autofac.Agents;
using Autofac.Agents.Knowledge;
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

    [Fact]
    public void ProviderMock_ResolvesMockClient_EvenWithoutApiKey()
    {
        using var provider = (ServiceProvider)Build(("Anthropic:Provider", "mock"));
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ILanguageModelClient>();

        Assert.IsType<MockLanguageModelClient>(client);
    }

    [Fact]
    public void ProviderAnthropicWithoutKey_StillSelectsAnthropic()
    {
        // Explicit provider=anthropic resolves the real client even without a key
        // (it surfaces a clear "not configured" failure at call time, not a silent mock).
        using var provider = (ServiceProvider)Build(("Anthropic:Provider", "anthropic"));
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ILanguageModelClient>();

        Assert.IsType<AnthropicLanguageModelClient>(client);
    }

    [Fact]
    public void KnowledgeRetriever_ResolvesConfiguredCorpus()
    {
        using var provider = (ServiceProvider)Build(
            ("Knowledge:Documents:0:Source", "quickstart.md"),
            ("Knowledge:Documents:0:Text", "Quickstart runs with Docker Compose."));

        var retriever = provider.GetRequiredService<IKnowledgeRetriever>();
        var results = retriever.Search("docker quickstart", topK: 1);

        var snippet = Assert.Single(results);
        Assert.Equal("quickstart.md", snippet.Source);
    }
}

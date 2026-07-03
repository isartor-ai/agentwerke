using Agentwerke.Agents.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents.Tests;

public sealed class KnowledgeRetrieverTests
{
    private static LexicalKnowledgeRetriever Retriever() => new(
    [
        new KnowledgeDocument("deploy.md", "How to deploy the service to production using Helm and Kubernetes."),
        new KnowledgeDocument("auth.md", "Authentication uses OIDC JWT bearer tokens and role mapping."),
        new KnowledgeDocument("empty.md", string.Empty),
    ]);

    [Fact]
    public void Search_RanksRelevantDocumentHighest()
    {
        var results = Retriever().Search("how do I deploy with kubernetes", topK: 2);

        Assert.NotEmpty(results);
        Assert.Equal("deploy.md", results[0].Source);
    }

    [Fact]
    public void Search_RespectsTopK()
    {
        var results = Retriever().Search("deploy kubernetes authentication oidc", topK: 1);
        Assert.Single(results);
    }

    [Fact]
    public void Search_EmptyQueryOrCorpus_ReturnsEmpty()
    {
        Assert.Empty(Retriever().Search(string.Empty, 3));
        Assert.Empty(new LexicalKnowledgeRetriever(Array.Empty<KnowledgeDocument>()).Search("deploy", 3));
    }

    [Fact]
    public void Search_IrrelevantQuery_ReturnsEmpty()
    {
        Assert.Empty(Retriever().Search("xylophone zebra", 3));
    }

    [Fact]
    public void LexicalKnowledgeRetriever_HasSinglePublicConstructor_SoDiIsUnambiguous()
    {
        // Two public constructors (IOptions + IEnumerable) make the DI container fail with an
        // ambiguous-constructor error at startup, because IEnumerable<T> resolves to empty.
        // The test/corpus constructor must stay internal.
        Assert.Single(typeof(LexicalKnowledgeRetriever).GetConstructors());
    }

    [Fact]
    public void LexicalKnowledgeRetriever_ResolvesFromContainerWithValidation()
    {
        // Reproduces the startup path: container activation with ValidateOnBuild, which is
        // what surfaced the ambiguous-constructor failure in production.
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<KnowledgeOptions>>(Options.Create(new KnowledgeOptions()));
        services.AddSingleton<IKnowledgeRetriever, LexicalKnowledgeRetriever>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider.GetRequiredService<IKnowledgeRetriever>());
    }
}

using Agentwerke.Agents.Knowledge;
using Agentwerke.Agents.Tools;

namespace Agentwerke.Agents.Tests;

public sealed class KnowledgeSearchToolTests
{
    private static AgentToolExecutionContext Context() =>
        new("run", "step", "agent", "knowledge.search", null, "general", "tag", 1);

    private static KnowledgeSearchTool Tool() => new(new LexicalKnowledgeRetriever(
    [
        new KnowledgeDocument("deploy.md", "Deploy with Helm to Kubernetes."),
    ]));

    [Fact]
    public async Task ExecuteAsync_ReturnsSnippetsWithCitations()
    {
        var result = await Tool().ExecuteAsync(
            Context(),
            new Dictionary<string, string> { ["query"] = "deploy kubernetes" },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("deploy.md", result.Output);
        Assert.Contains("Deploy with Helm", result.Output!);
    }

    [Fact]
    public void Validate_MissingQuery_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Tool().Validate(new Dictionary<string, string>()));
    }

    [Fact]
    public async Task ExecuteAsync_NoMatch_ReturnsNoResultsMessage()
    {
        var result = await Tool().ExecuteAsync(
            Context(),
            new Dictionary<string, string> { ["query"] = "xylophone" },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("No relevant knowledge", result.Output!);
    }
}

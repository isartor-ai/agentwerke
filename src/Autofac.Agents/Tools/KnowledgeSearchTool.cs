using System.Globalization;
using System.Text;
using Autofac.Agents.Knowledge;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Tools;

/// <summary>
/// Policy-gated retrieval tool (#176): an agent searches the knowledge base and gets back
/// ranked snippets with source citations, recorded like any other tool invocation. Runs
/// through the Tool Gateway, so it is subject to the same allow/deny and policy decisions.
/// </summary>
public sealed class KnowledgeSearchTool : IAgentTool, IToolSchemaProvider
{
    private const int DefaultTopK = 3;
    private const int MaxTopK = 10;

    private readonly IKnowledgeRetriever _retriever;

    public KnowledgeSearchTool(IKnowledgeRetriever retriever)
    {
        _retriever = retriever;
    }

    public string Name => "knowledge.search";

    public string Category => AgentToolCategories.Knowledge;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("query", "string", "The question or query to retrieve relevant context for.", Required: true),
        new("top_k", "integer", "Maximum number of snippets to return (1-10, default 3).", Required: false),
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        if (!input.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("Tool input is missing required field 'query'.");
        }
    }

    public Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var query = input["query"];
        var topK = DefaultTopK;
        if (input.TryGetValue("top_k", out var rawTopK)
            && int.TryParse(rawTopK, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            topK = Math.Clamp(parsed, 1, MaxTopK);
        }

        var snippets = _retriever.Search(query, topK);
        if (snippets.Count == 0)
        {
            return Task.FromResult(new AgentToolExecutionResult(
                Succeeded: true,
                Output: "No relevant knowledge was found.",
                FailureReason: null));
        }

        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Found {snippets.Count} relevant snippet(s):");
        var index = 1;
        foreach (var snippet in snippets)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"[{index}] source: {snippet.Source} (score {snippet.Score:0.00})");
            builder.AppendLine(snippet.Text);
            builder.AppendLine();
            index++;
        }

        return Task.FromResult(new AgentToolExecutionResult(
            Succeeded: true,
            Output: builder.ToString().TrimEnd(),
            FailureReason: null));
    }
}

using Microsoft.Extensions.Options;

namespace Autofac.Agents.Knowledge;

public sealed record KnowledgeDocument(string Source, string Text);

public sealed record KnowledgeSnippet(string Source, string Text, double Score);

/// <summary>
/// Retrieves context relevant to a query, with provenance, for agents to ground on (#176).
/// The backing store is pluggable — a lexical corpus today, a vector/embeddings store later.
/// </summary>
public interface IKnowledgeRetriever
{
    IReadOnlyList<KnowledgeSnippet> Search(string query, int topK);
}

public sealed class KnowledgeOptions
{
    public const string Section = "Knowledge";

    /// <summary>In-memory corpus the default lexical retriever searches.</summary>
    public List<KnowledgeDocumentOptions> Documents { get; set; } = [];

    public int DefaultTopK { get; set; } = 3;
}

public sealed class KnowledgeDocumentOptions
{
    public string Source { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Default retriever: deterministic term-overlap scoring over a configured in-memory corpus.
/// Dependency-free so RAG works out of the box; a pgvector/embeddings retriever can replace it
/// behind <see cref="IKnowledgeRetriever"/> without touching the agent-facing tool.
/// </summary>
public sealed class LexicalKnowledgeRetriever : IKnowledgeRetriever
{
    private static readonly char[] Separators =
        [' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-'];

    private readonly IReadOnlyList<KnowledgeDocument> _corpus;

    public LexicalKnowledgeRetriever(IOptions<KnowledgeOptions> options)
        : this((options.Value.Documents ?? []).Select(d => new KnowledgeDocument(d.Source, d.Text)))
    {
    }

    // Internal (test) constructor: must NOT be public, or DI sees two activatable
    // constructors (IEnumerable&lt;T&gt; resolves to empty) and fails with an ambiguous-ctor
    // error at container build. The IOptions ctor above is the single public/DI ctor.
    internal LexicalKnowledgeRetriever(IEnumerable<KnowledgeDocument> corpus)
    {
        _corpus = corpus.Where(d => !string.IsNullOrWhiteSpace(d.Text)).ToArray();
    }

    public IReadOnlyList<KnowledgeSnippet> Search(string query, int topK)
    {
        var queryTerms = Tokenize(query).Distinct().ToArray();
        if (queryTerms.Length == 0 || _corpus.Count == 0 || topK <= 0)
        {
            return [];
        }

        return _corpus
            .Select(doc => new { doc, score = Score(queryTerms, Tokenize(doc.Text)) })
            .Where(scored => scored.score > 0)
            .OrderByDescending(scored => scored.score)
            .Take(topK)
            .Select(scored => new KnowledgeSnippet(scored.doc.Source, scored.doc.Text, scored.score))
            .ToArray();
    }

    private static double Score(IReadOnlyCollection<string> queryTerms, IEnumerable<string> docTerms)
    {
        var docSet = new HashSet<string>(docTerms);
        if (docSet.Count == 0)
        {
            return 0;
        }

        var matches = queryTerms.Count(docSet.Contains);
        return (double)matches / queryTerms.Count; // fraction of query terms present in the doc
    }

    private static IEnumerable<string> Tokenize(string? text) =>
        (text ?? string.Empty)
            .ToLowerInvariant()
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2);
}

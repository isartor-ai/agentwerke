using System.Text;

namespace Agentwerke.Agents.Models;

internal static class LanguageModelReasoningParser
{
    private const string StartTag = "<agent_reasoning>";
    private const string EndTag = "</agent_reasoning>";

    // Reasoning models (DeepSeek R1, Qwen thinking variants, GLM, …) emit their chain of thought
    // inside <think>…</think> at the start of the content when they don't use a separate
    // reasoning_content field. Split it out so the reasoning streams to the UI and the visible
    // output stays clean.
    private const string ThinkStartTag = "<think>";
    private const string ThinkEndTag = "</think>";

    /// <summary>
    /// Splits a completed content string into its visible output and the reasoning it embedded via
    /// <c>&lt;think&gt;</c> tags. Unclosed <c>&lt;think&gt;</c> (model still mid-thought) treats the
    /// remainder as reasoning with no output yet.
    /// </summary>
    public static ParsedReasoningOutput ExtractThink(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new ParsedReasoningOutput(output, null);
        }

        var start = output.IndexOf(ThinkStartTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return new ParsedReasoningOutput(output, null);
        }

        var contentStart = start + ThinkStartTag.Length;
        var end = output.IndexOf(ThinkEndTag, contentStart, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            var openReasoning = output[contentStart..].Trim();
            var beforeThink = output[..start].Trim();
            return new ParsedReasoningOutput(
                string.IsNullOrWhiteSpace(beforeThink) ? null : beforeThink,
                string.IsNullOrWhiteSpace(openReasoning) ? null : openReasoning);
        }

        var reasoning = output[contentStart..end].Trim();
        var cleaned = string.Concat(output.AsSpan(0, start), output.AsSpan(end + ThinkEndTag.Length)).Trim();
        return new ParsedReasoningOutput(
            string.IsNullOrWhiteSpace(cleaned) ? null : cleaned,
            string.IsNullOrWhiteSpace(reasoning) ? null : reasoning);
    }

    public static ParsedReasoningOutput Extract(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new ParsedReasoningOutput(output, null);
        }

        var start = output.IndexOf(StartTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return new ParsedReasoningOutput(output, null);
        }

        var contentStart = start + StartTag.Length;
        var end = output.IndexOf(EndTag, contentStart, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return new ParsedReasoningOutput(output, null);
        }

        var reasoning = output[contentStart..end].Trim();
        var cleaned = string.Concat(output.AsSpan(0, start), output.AsSpan(end + EndTag.Length)).Trim();

        return new ParsedReasoningOutput(
            string.IsNullOrWhiteSpace(cleaned) ? null : cleaned,
            string.IsNullOrWhiteSpace(reasoning) ? null : reasoning);
    }

    public static string? ExtractVisibleSummary(string? output, bool allowPlainTextFallback = false)
    {
        var parsed = Extract(output);
        if (!string.IsNullOrWhiteSpace(parsed.ReasoningSummary))
        {
            return parsed.ReasoningSummary;
        }

        if (allowPlainTextFallback && !string.IsNullOrWhiteSpace(parsed.Output))
        {
            return parsed.Output;
        }

        return null;
    }

    public static string? ExtractLatestVisibleSummaryCandidate(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var start = output.LastIndexOf(StartTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var contentStart = start + StartTag.Length;
        var end = output.IndexOf(EndTag, contentStart, StringComparison.OrdinalIgnoreCase);
        var reasoning = end >= 0
            ? output[contentStart..end]
            : StripTrailingPartialEndTag(output[contentStart..]);

        var trimmed = reasoning.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string StripTrailingPartialEndTag(string content)
    {
        for (var length = EndTag.Length - 1; length > 0; length--)
        {
            var partialTag = EndTag[..length];
            if (content.EndsWith(partialTag, StringComparison.OrdinalIgnoreCase))
            {
                return content[..^length];
            }
        }

        return content;
    }
}

internal sealed record ParsedReasoningOutput(string? Output, string? ReasoningSummary);

internal sealed class StreamingReasoningSummaryAccumulator
{
    private readonly StringBuilder _buffer = new();

    public string? LatestSummary { get; private set; }

    public void AppendText(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _buffer.Append(text);
        }
    }

    public string? TakeNextSummary()
    {
        var candidate = LanguageModelReasoningParser.ExtractLatestVisibleSummaryCandidate(_buffer.ToString());
        if (string.IsNullOrWhiteSpace(candidate)
            || string.Equals(candidate, LatestSummary, StringComparison.Ordinal))
        {
            return null;
        }

        LatestSummary = candidate;
        return candidate;
    }
}

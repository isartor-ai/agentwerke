namespace Agentwerke.Agents.Models;

internal static class LanguageModelReasoningParser
{
    private const string StartTag = "<agent_reasoning>";
    private const string EndTag = "</agent_reasoning>";

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
}

internal sealed record ParsedReasoningOutput(string? Output, string? ReasoningSummary);

using System.Text;
using System.Text.Json;
using Agentwerke.Sandboxes;
using Agentwerke.Workflows.Runtime;

namespace Agentwerke.Agents.Models;

public static class SandboxProgressMessageCodec
{
    private const string Prefix = "__AGENTWERKE_PROGRESS_B64__";
    private const string Suffix = "__END_AGENTWERKE_PROGRESS__";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(AgentExecutionProgressUpdate update)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(update, JsonOptions)));
        return $"{Prefix}{payload}{Suffix}";
    }

    public static bool TryDecode(string payload, out AgentExecutionProgressUpdate? update)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            update = JsonSerializer.Deserialize<AgentExecutionProgressUpdate>(json, JsonOptions);
            return update is not null;
        }
        catch
        {
            update = null;
            return false;
        }
    }

    public static string BuildStableKey(AgentExecutionProgressUpdate update) =>
        string.Join(
            "|",
            update.Kind,
            update.ToolName ?? string.Empty,
            update.ToolCallId ?? string.Empty,
            update.Status ?? string.Empty,
            update.Summary?.Trim() ?? string.Empty);

    public sealed class StreamParser
    {
        private readonly StringBuilder _buffer = new();

        public IReadOnlyList<AgentExecutionProgressUpdate> Read(string? chunk, out string cleanedText)
        {
            if (!string.IsNullOrEmpty(chunk))
            {
                _buffer.Append(chunk);
            }

            var updates = new List<AgentExecutionProgressUpdate>();
            var cleaned = new StringBuilder();

            while (true)
            {
                var current = _buffer.ToString();
                var prefixIndex = current.IndexOf(Prefix, StringComparison.Ordinal);
                if (prefixIndex < 0)
                {
                    var tailStart = Math.Max(
                        current.LastIndexOf('\n') + 1,
                        Math.Max(0, current.Length - (Prefix.Length - 1)));
                    var emitLength = tailStart;
                    if (emitLength > 0)
                    {
                        cleaned.Append(current.AsSpan(0, emitLength));
                        _buffer.Remove(0, emitLength);
                    }

                    break;
                }

                if (prefixIndex > 0)
                {
                    cleaned.Append(current.AsSpan(0, prefixIndex));
                    _buffer.Remove(0, prefixIndex);
                    current = _buffer.ToString();
                    prefixIndex = 0;
                }

                var suffixIndex = current.IndexOf(Suffix, Prefix.Length, StringComparison.Ordinal);
                if (suffixIndex < 0)
                {
                    break;
                }

                var encodedPayload = current.Substring(Prefix.Length, suffixIndex - Prefix.Length);
                if (TryDecode(encodedPayload, out var update) && update is not null)
                {
                    updates.Add(update);
                }

                var removeLength = suffixIndex + Suffix.Length;
                if (current.Length > removeLength && current[removeLength] == '\r')
                {
                    removeLength++;
                }
                if (current.Length > removeLength && current[removeLength] == '\n')
                {
                    removeLength++;
                }

                _buffer.Remove(0, removeLength);
            }

            cleanedText = cleaned.ToString();
            return updates;
        }

        public string Flush()
        {
            var remaining = _buffer.ToString();
            _buffer.Clear();
            return remaining;
        }
    }

    public static IReadOnlyList<SandboxLogEntry> RemoveMarkers(
        IReadOnlyList<SandboxLogEntry>? entries,
        Action<AgentExecutionProgressUpdate>? onUpdate = null)
    {
        if (entries is null || entries.Count == 0)
        {
            return [];
        }

        var parser = new StreamParser();
        var cleanedEntries = new List<SandboxLogEntry>(entries.Count);

        foreach (var entry in entries)
        {
            var updates = parser.Read(entry.Message, out var cleanedMessage);
            foreach (var update in updates)
            {
                onUpdate?.Invoke(update);
            }

            if (!string.IsNullOrEmpty(cleanedMessage))
            {
                cleanedEntries.Add(entry with { Message = cleanedMessage });
            }
        }

        var trailing = parser.Flush();
        if (!string.IsNullOrEmpty(trailing))
        {
            if (cleanedEntries.Count > 0)
            {
                var lastEntry = cleanedEntries[^1];
                cleanedEntries[^1] = lastEntry with
                {
                    Message = lastEntry.Message + trailing
                };
            }
            else
            {
                var timestamp = entries[^1].Timestamp;
                var stream = entries[^1].Stream;
                cleanedEntries.Add(new SandboxLogEntry(stream, trailing, timestamp));
            }
        }

        return cleanedEntries;
    }

    public static string RemoveMarkers(string? text, Action<AgentExecutionProgressUpdate>? onUpdate = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var parser = new StreamParser();
        var updates = parser.Read(text, out var cleanedText);
        foreach (var update in updates)
        {
            onUpdate?.Invoke(update);
        }

        return cleanedText + parser.Flush();
    }
}

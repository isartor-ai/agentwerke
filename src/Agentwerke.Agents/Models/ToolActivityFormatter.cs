using System.Text;
using Agentwerke.Domain.AgentRuntime;

namespace Agentwerke.Agents.Models;

/// <summary>
/// Produces a concise, redacted one-line description of a concrete tool action — the file edited,
/// the command run, the PR opened — for the live run timeline. Only safe metadata (paths, commands,
/// sizes, ids) is surfaced; large content and anything that looks secret is never included, so this
/// is never a channel for leaking file bodies or tokens.
/// </summary>
internal static class ToolActivityFormatter
{
    private const int MaxValueLength = 120;

    // Never surface the value of these keys — they carry content bodies or credentials.
    private static readonly HashSet<string> RedactedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "content", "file_text", "text", "body", "patch", "diff", "token",
        "password", "secret", "api_key", "apikey", "authorization",
    };

    // Run-scoped plumbing the model didn't choose — not interesting in an activity log.
    private static readonly HashSet<string> IgnoredKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "run_id", "step_id", "attempt",
    };

    /// <summary>Detail for a tool call about to run, derived from its arguments.</summary>
    public static string? DescribeInput(string toolName, IReadOnlyDictionary<string, string> input)
    {
        var name = toolName.ToLowerInvariant();

        // Tool-specific shapes read best; fall through to a generic key=value summary otherwise.
        if (name.Contains("file_write") || name.Contains("file_edit") || name.Contains("file_read")
            || name.Contains("write_file") || name.Contains("edit_file") || name.Contains("read_file"))
        {
            var path = First(input, "path", "file", "file_path", "filename");
            if (path is not null)
            {
                var size = ByteSizeOf(input, "content", "file_text", "text");
                return size is not null ? $"{path} ({size})" : path;
            }
        }
        else if (name.Contains("shell") || name.Contains("bash") || name.Contains("exec") || name.Contains("command"))
        {
            var command = First(input, "command", "cmd", "script");
            if (command is not null)
            {
                return Truncate(command.ReplaceLineEndings(" "));
            }
        }
        else if (name.Contains("git"))
        {
            var git = First(input, "command", "args", "operation", "action");
            if (git is not null)
            {
                return Truncate(git);
            }
        }
        else if (name.Contains("pull_request") || name.Contains("create_pr"))
        {
            var title = First(input, "title");
            var head = First(input, "head", "branch", "head_branch");
            var parts = new List<string>();
            if (head is not null) parts.Add(head);
            if (title is not null) parts.Add($"“{Truncate(title, 60)}”");
            if (parts.Count > 0) return string.Join(" ", parts);
        }
        else if (name.Contains("comment") || name.Contains("issue") || name.Contains("review"))
        {
            var issue = First(input, "issue_number", "number", "pull_number");
            if (issue is not null) return $"#{issue}";
        }

        return GenericSummary(input);
    }

    /// <summary>Detail for a completed tool call, derived from its recorded outcome.</summary>
    public static string? DescribeResult(AgentToolInvocationRecord invocation)
    {
        if (!string.IsNullOrWhiteSpace(invocation.ErrorMessage))
        {
            return Truncate(invocation.ErrorMessage.ReplaceLineEndings(" "));
        }

        var pieces = new List<string>();
        if (invocation.ArtifactNames.Count > 0)
        {
            pieces.Add(invocation.ArtifactNames.Count == 1
                ? invocation.ArtifactNames[0]
                : $"{invocation.ArtifactNames.Count} artifacts");
        }

        if (!string.IsNullOrWhiteSpace(invocation.OutputSummary))
        {
            pieces.Add(Truncate(invocation.OutputSummary.ReplaceLineEndings(" ")));
        }

        return pieces.Count == 0 ? null : string.Join(" · ", pieces);
    }

    private static string? First(IReadOnlyDictionary<string, string> input, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (input.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? ByteSizeOf(IReadOnlyDictionary<string, string> input, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (input.TryGetValue(key, out var value) && value is not null)
            {
                return FormatBytes(Encoding.UTF8.GetByteCount(value));
            }
        }

        return null;
    }

    private static string? GenericSummary(IReadOnlyDictionary<string, string> input)
    {
        var parts = new List<string>();
        foreach (var (key, value) in input)
        {
            if (IgnoredKeys.Contains(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (RedactedKeys.Contains(key))
            {
                parts.Add($"{key}=({FormatBytes(Encoding.UTF8.GetByteCount(value))})");
            }
            else
            {
                parts.Add($"{key}={Truncate(value.ReplaceLineEndings(" "), 48)}");
            }

            if (parts.Count == 3)
            {
                break;
            }
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes} B";

    private static string Truncate(string value, int max = MaxValueLength) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";
}

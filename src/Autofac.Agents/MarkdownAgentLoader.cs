using System.Security.Cryptography;
using System.Text;

namespace Autofac.Agents;

/// <summary>
/// Loads agent profiles from <c>agents/&lt;id&gt;/AGENT.md</c> files. The YAML
/// frontmatter holds the machine-readable profile; the Markdown body is the
/// agent's system prompt. Mirrors <see cref="Skills.MarkdownSkillLoader"/>.
/// </summary>
public static class MarkdownAgentLoader
{
    private const string AgentFileName = "AGENT.md";
    private const string FrontmatterDelimiter = "---";

    /// <summary>
    /// Scans <paramref name="directory"/> for subdirectories each containing an
    /// AGENT.md, parses them, and returns one profile per agent. Subdirectories
    /// without an AGENT.md are silently skipped.
    /// </summary>
    public static IReadOnlyList<AgentProfile> LoadFromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var profiles = new List<AgentProfile>();

        foreach (var subDir in Directory.EnumerateDirectories(directory).OrderBy(static d => d, StringComparer.OrdinalIgnoreCase))
        {
            var agentFile = Path.Combine(subDir, AgentFileName);
            if (!File.Exists(agentFile))
            {
                continue;
            }

            var rawContent = File.ReadAllText(agentFile, Encoding.UTF8);
            profiles.Add(Parse(Path.GetFileName(subDir), rawContent));
        }

        return profiles.AsReadOnly();
    }

    public static AgentProfile Parse(string directoryId, string rawContent)
    {
        var fingerprint = ComputeFingerprint(rawContent);
        var (frontmatter, body) = SplitFrontmatter(rawContent);
        var meta = ParseFrontmatter(frontmatter);

        var id = meta.Scalar("id") ?? directoryId;
        var supportedActions = meta.List("supportedActions");

        // Each declared skill becomes a descriptive ref carrying the agent's
        // supported actions. SkillManifestId is intentionally left unset so runs
        // don't fail when the skills directory has no matching manifest yet.
        var skills = meta.List("skills")
            .Select(skillId => new AgentSkillRef(skillId, skillId, string.Empty, supportedActions))
            .ToList();

        return new AgentProfile
        {
            AgentId = id,
            Name = meta.Scalar("name") ?? id,
            Description = meta.Scalar("description") ?? string.Empty,
            Category = meta.Scalar("category") ?? string.Empty,
            Runner = meta.Scalar("runner") ?? "agent-model",
            Model = meta.Scalar("model"),
            DockerImage = meta.Scalar("dockerImage"),
            Network = meta.Scalar("network") ?? "none",
            Skills = skills,
            Tools = meta.List("tools"),
            DeniedTools = meta.List("deniedTools"),
            Secrets = meta.List("secrets"),
            SupportedActions = supportedActions,
            SupportedEnvironments = meta.List("supportedEnvironments"),
            SupportedPolicyTags = meta.List("supportedPolicyTags"),
            SystemPrompt = string.IsNullOrWhiteSpace(body) ? null : body.Trim(),
            Fingerprint = fingerprint,
            Source = "file"
        };
    }

    private static (string Frontmatter, string Body) SplitFrontmatter(string raw)
    {
        var lines = raw.ReplaceLineEndings("\n").Split('\n');

        if (lines.Length < 2 || lines[0].Trim() != FrontmatterDelimiter)
        {
            return (string.Empty, raw);
        }

        var closingIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == FrontmatterDelimiter)
            {
                closingIndex = i;
                break;
            }
        }

        if (closingIndex < 0)
        {
            return (string.Empty, raw);
        }

        var frontmatter = string.Join('\n', lines[1..closingIndex]);
        var body = string.Join('\n', lines[(closingIndex + 1)..]);
        return (frontmatter, body);
    }

    private static Frontmatter ParseFrontmatter(string frontmatter)
    {
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(frontmatter))
        {
            return new Frontmatter(scalars, lists);
        }

        string? currentListKey = null;

        foreach (var line in frontmatter.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            if ((line.StartsWith("  - ", StringComparison.Ordinal) || line.StartsWith("- ", StringComparison.Ordinal)) &&
                currentListKey is not null)
            {
                var item = Unquote(trimmed[2..].Trim());
                if (!string.IsNullOrEmpty(item))
                {
                    lists[currentListKey].Add(item);
                }

                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var value = line[(colon + 1)..].Trim();
            currentListKey = null;

            if (string.IsNullOrEmpty(value))
            {
                currentListKey = key;
                lists[key] = [];
                continue;
            }

            if (TryParseInlineList(value, out var inline))
            {
                lists[key] = inline;
                continue;
            }

            scalars[key] = Unquote(value);
        }

        return new Frontmatter(scalars, lists);
    }

    private static bool TryParseInlineList(string value, out List<string> items)
    {
        items = [];

        if (!(value.StartsWith('[') && value.EndsWith(']')))
        {
            return false;
        }

        var inner = value[1..^1];
        foreach (var item in inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var unquoted = Unquote(item);
            if (!string.IsNullOrEmpty(unquoted))
            {
                items.Add(unquoted);
            }
        }

        return true;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static string ComputeFingerprint(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record Frontmatter(
        IReadOnlyDictionary<string, string> Scalars,
        IReadOnlyDictionary<string, List<string>> Lists)
    {
        public string? Scalar(string key) =>
            Scalars.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        public IReadOnlyList<string> List(string key) =>
            Lists.TryGetValue(key, out var v) ? v.AsReadOnly() : [];
    }
}

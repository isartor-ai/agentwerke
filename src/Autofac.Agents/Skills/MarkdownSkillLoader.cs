using System.Security.Cryptography;
using System.Text;

namespace Autofac.Agents.Skills;

public static class MarkdownSkillLoader
{
    private const string SkillFileName = "SKILL.md";
    private const string FrontmatterDelimiter = "---";

    /// <summary>
    /// Scans <paramref name="directory"/> for subdirectories each containing a SKILL.md,
    /// parses frontmatter, and returns a manifest per skill found.
    /// Subdirectories without a SKILL.md are silently skipped.
    /// </summary>
    public static IReadOnlyList<SkillManifest> LoadFromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var manifests = new List<SkillManifest>();

        foreach (var subDir in Directory.EnumerateDirectories(directory).OrderBy(static d => d, StringComparer.OrdinalIgnoreCase))
        {
            var skillFile = Path.Combine(subDir, SkillFileName);
            if (!File.Exists(skillFile))
            {
                continue;
            }

            var rawContent = File.ReadAllText(skillFile, Encoding.UTF8);
            var manifest = Parse(
                skillId: Path.GetFileName(subDir),
                filePath: Path.GetFullPath(skillFile),
                rawContent: rawContent);

            if (manifest is not null)
            {
                manifests.Add(manifest);
            }
        }

        return manifests.AsReadOnly();
    }

    public static SkillManifest? Parse(string skillId, string filePath, string rawContent)
    {
        var fingerprint = ComputeFingerprint(rawContent);
        var (frontmatter, body) = SplitFrontmatter(rawContent);

        var name = ExtractField(frontmatter, "name") ?? skillId;
        var description = ExtractField(frontmatter, "description") ?? string.Empty;

        return new SkillManifest(
            SkillId: skillId,
            Name: name,
            Description: description,
            Content: body.Trim(),
            Fingerprint: fingerprint,
            FilePath: filePath);
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

    private static string? ExtractField(string frontmatter, string key)
    {
        if (string.IsNullOrWhiteSpace(frontmatter))
        {
            return null;
        }

        foreach (var line in frontmatter.Split('\n'))
        {
            var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex < 0)
            {
                continue;
            }

            var lineKey = line[..colonIndex].Trim();
            if (!string.Equals(lineKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(colonIndex + 1)..].Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }

    private static string ComputeFingerprint(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

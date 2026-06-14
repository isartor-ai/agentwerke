using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Autofac.Agents.Skills;

public static class MarkdownSkillLoader
{
    private const string SkillFileName = "SKILL.md";
    private const string FrontmatterDelimiter = "---";
    private static readonly Regex SkillVersionPattern = new("^[0-9A-Za-z][0-9A-Za-z.+_-]*$", RegexOptions.CultureInvariant);

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
        var metadata = ParseFrontmatter(frontmatter);

        var name = metadata.GetScalar("name") ?? skillId;
        var description = metadata.GetScalar("description") ?? string.Empty;
        var version = metadata.GetScalar("version");
        var invocationRules = metadata.GetList("invocationRules");
        var requiredFiles = metadata.GetList("requiredFiles");
        var optionalTools = metadata.GetList("optionalTools");

        ValidateManifest(skillId, filePath, version, requiredFiles);

        return new SkillManifest(
            SkillId: skillId,
            Name: name,
            Description: description,
            Version: version,
            InvocationRules: invocationRules,
            RequiredFiles: requiredFiles,
            OptionalTools: optionalTools,
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

    private static SkillFrontmatter ParseFrontmatter(string frontmatter)
    {
        if (string.IsNullOrWhiteSpace(frontmatter))
        {
            return SkillFrontmatter.Empty;
        }

        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentListKey = null;

        foreach (var line in frontmatter.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith('#'))
            {
                continue;
            }

            if ((line.StartsWith("  - ", StringComparison.Ordinal) || line.StartsWith("- ", StringComparison.Ordinal)) &&
                currentListKey is not null)
            {
                var listItem = trimmedLine[2..].Trim();
                if (!string.IsNullOrEmpty(listItem))
                {
                    lists[currentListKey].Add(listItem);
                }

                continue;
            }

            var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex < 0)
            {
                continue;
            }

            var lineKey = line[..colonIndex].Trim();
            if (string.IsNullOrEmpty(lineKey))
            {
                continue;
            }

            var value = line[(colonIndex + 1)..].Trim();
            currentListKey = null;

            if (string.IsNullOrEmpty(value))
            {
                currentListKey = lineKey;
                lists[currentListKey] = [];
                continue;
            }

            if (TryParseInlineList(value, out var inlineValues))
            {
                lists[lineKey] = inlineValues;
                continue;
            }

            scalars[lineKey] = value;
        }

        return new SkillFrontmatter(scalars, lists);
    }

    private static bool TryParseInlineList(string value, out List<string> items)
    {
        items = [];

        if (!(value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal)))
        {
            return false;
        }

        var inner = value[1..^1];
        foreach (var item in inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrEmpty(item))
            {
                items.Add(item);
            }
        }

        return true;
    }

    private static void ValidateManifest(
        string skillId,
        string filePath,
        string? version,
        IReadOnlyList<string> requiredFiles)
    {
        if (!string.IsNullOrWhiteSpace(version) && !SkillVersionPattern.IsMatch(version))
        {
            throw new InvalidOperationException(
                $"Skill '{skillId}' at '{filePath}' declares invalid version '{version}'. Use a non-empty version token such as '1.0.0' or '1.0.0-beta.1'.");
        }

        var skillDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
        foreach (var requiredFile in requiredFiles)
        {
            var resolvedPath = Path.IsPathRooted(requiredFile)
                ? requiredFile
                : Path.GetFullPath(Path.Combine(skillDirectory, requiredFile));

            if (!File.Exists(resolvedPath))
            {
                throw new InvalidOperationException(
                    $"Skill '{skillId}' requires file '{requiredFile}', but it was not found at '{resolvedPath}'.");
            }
        }
    }

    private static string ComputeFingerprint(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record SkillFrontmatter(
        IReadOnlyDictionary<string, string> Scalars,
        IReadOnlyDictionary<string, List<string>> Lists)
    {
        public static SkillFrontmatter Empty { get; } =
            new(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));

        public string? GetScalar(string key) =>
            Scalars.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;

        public IReadOnlyList<string> GetList(string key) =>
            Lists.TryGetValue(key, out var values)
                ? values.AsReadOnly()
                : [];
    }
}

using System.Linq;
using System.Text;
using System.Text.Json;

namespace Agentwerke.Agents;

/// <summary>
/// Serializes an <see cref="AgentProfile"/> back to AGENT.md frontmatter + body format.
/// </summary>
public static class AgentMarkdownSerializer
{
    private static readonly JsonSerializerOptions SkillBindingJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize(AgentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {profile.AgentId}");
        sb.AppendLine($"name: {Escape(profile.Name)}");
        sb.AppendLine($"description: {Escape(profile.Description)}");
        sb.AppendLine($"category: {profile.Category}");
        sb.AppendLine($"runner: {profile.Runner}");
        if (!string.IsNullOrWhiteSpace(profile.Model))
        {
            sb.AppendLine($"model: {profile.Model}");
        }

        if (!string.IsNullOrWhiteSpace(profile.DockerImage))
        {
            sb.AppendLine($"dockerImage: {profile.DockerImage}");
        }

        if (!string.IsNullOrWhiteSpace(profile.Network) &&
            !string.Equals(profile.Network, "none", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"network: {profile.Network}");
        }

        AppendList(sb, "skills", profile.Skills.Select(static skill => skill.SkillId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        AppendSkillBindings(sb, profile.Skills);
        AppendList(sb, "tools", profile.Tools);
        AppendList(sb, "deniedTools", profile.DeniedTools);
        AppendList(sb, "secrets", profile.Secrets);
        AppendList(sb, "supportedActions", profile.SupportedActions);
        AppendList(sb, "supportedEnvironments", profile.SupportedEnvironments);
        AppendList(sb, "supportedPolicyTags", profile.SupportedPolicyTags);
        AppendList(sb, "sandboxProfiles", profile.SandboxProfiles);
        sb.AppendLine("---");

        if (!string.IsNullOrWhiteSpace(profile.SystemPrompt))
        {
            sb.AppendLine();
            sb.AppendLine(profile.SystemPrompt.Trim());
        }

        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, string key, IReadOnlyCollection<string> items)
    {
        if (items.Count == 0)
        {
            sb.AppendLine($"{key}: []");
            return;
        }

        sb.AppendLine($"{key}:");
        foreach (var item in items)
        {
            sb.AppendLine($"  - {item}");
        }
    }

    private static void AppendSkillBindings(StringBuilder sb, IReadOnlyList<AgentSkillRef> skills)
    {
        if (skills.Count == 0)
        {
            sb.AppendLine("skillBindings: []");
            return;
        }

        sb.AppendLine("skillBindings:");
        foreach (var skill in skills)
        {
            var payload = JsonSerializer.Serialize(new SkillBindingDocument(
                skill.SkillId,
                skill.Name,
                skill.Description,
                skill.SupportedActions.ToArray(),
                skill.SkillManifestId), SkillBindingJsonOptions);
            sb.AppendLine($"  - {payload}");
        }
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (value.Contains(':') ||
            value.Contains('\n') ||
            value.Contains('\r') ||
            value.StartsWith('"') ||
            value.StartsWith('\'') ||
            value != value.Trim())
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        return value;
    }

    private sealed record SkillBindingDocument(
        string SkillId,
        string Name,
        string Description,
        IReadOnlyList<string> SupportedActions,
        string? SkillManifestId);
}

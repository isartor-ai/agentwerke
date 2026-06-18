using System.Linq;
using System.Text;

namespace Autofac.Agents;

/// <summary>
/// Serializes an <see cref="AgentProfile"/> back to AGENT.md frontmatter + body format.
/// </summary>
public static class AgentMarkdownSerializer
{
    public static string Serialize(AgentProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {p.AgentId}");
        sb.AppendLine($"name: {Escape(p.Name)}");
        sb.AppendLine($"description: {Escape(p.Description)}");
        sb.AppendLine($"category: {p.Category}");
        sb.AppendLine($"runner: {p.Runner}");
        if (!string.IsNullOrWhiteSpace(p.Model))
            sb.AppendLine($"model: {p.Model}");
        if (!string.IsNullOrWhiteSpace(p.DockerImage))
            sb.AppendLine($"dockerImage: {p.DockerImage}");
        if (!string.IsNullOrEmpty(p.Network) && p.Network != "none")
            sb.AppendLine($"network: {p.Network}");
        AppendList(sb, "skills", p.Skills.Select(static s => s.SkillId).ToList());
        AppendList(sb, "tools", p.Tools);
        AppendList(sb, "deniedTools", p.DeniedTools);
        AppendList(sb, "secrets", p.Secrets);
        AppendList(sb, "supportedActions", p.SupportedActions);
        AppendList(sb, "supportedEnvironments", p.SupportedEnvironments);
        AppendList(sb, "supportedPolicyTags", p.SupportedPolicyTags);
        sb.AppendLine("---");

        if (!string.IsNullOrWhiteSpace(p.SystemPrompt))
        {
            sb.AppendLine();
            sb.AppendLine(p.SystemPrompt.Trim());
        }

        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, string key, IReadOnlyCollection<string> items)
    {
        if (items.Count == 0)
        {
            sb.AppendLine($"{key}: []");
        }
        else
        {
            sb.AppendLine($"{key}:");
            foreach (var item in items)
                sb.AppendLine($"  - {item}");
        }
    }

    // Wrap in quotes if the value contains YAML-significant characters.
    private static string Escape(string value)
    {
        if (value.Contains(':') || value.StartsWith('"') || value.StartsWith('\''))
            return $"\"{value.Replace("\"", "\\\"")}\"";
        return value;
    }
}

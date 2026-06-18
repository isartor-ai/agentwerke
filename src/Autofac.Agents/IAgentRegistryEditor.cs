using System.Text;

namespace Autofac.Agents;

public interface IAgentRegistryEditor
{
    ManagedAgentDocument? Find(string agentId);

    ManagedAgentDocument Save(AgentProfile profile);

    ManagedAgentDocument Upload(string fileName, string content);
}

public sealed record ManagedAgentDocument(
    AgentProfile Profile,
    string RawMarkdown,
    string EffectiveFilePath,
    string? SourceFilePath);

public sealed class FileAgentRegistryEditor : IAgentRegistryEditor
{
    private const string InvalidAgentIdMessage = "Agent id may only contain letters, numbers, '.', '_' or '-'.";
    private readonly AgentRegistryPaths _paths;
    private readonly IAgentRegistry _registry;

    public FileAgentRegistryEditor(
        AgentRegistryPaths paths,
        IAgentRegistry registry)
    {
        _paths = paths;
        _registry = registry;
    }

    public ManagedAgentDocument? Find(string agentId)
    {
        var profile = _registry.Find(agentId);
        return profile is null ? null : ToDocument(profile);
    }

    public ManagedAgentDocument Save(AgentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.AgentId))
        {
            throw new InvalidOperationException("Agent id is required.");
        }

        var destinationPath = GetAgentFilePath(profile.AgentId);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var rawMarkdown = AgentMarkdownSerializer.Serialize(profile);
        File.WriteAllText(destinationPath, rawMarkdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return Find(profile.AgentId)
            ?? throw new InvalidOperationException($"Saved agent '{profile.AgentId}', but it could not be reloaded.");
    }

    public ManagedAgentDocument Upload(string fileName, string content)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Uploaded agent file must have a file name.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Uploaded agent file is empty.");
        }

        var directoryId = DeriveDirectoryId(fileName);
        var parsed = MarkdownAgentLoader.Parse(directoryId, content);
        if (string.IsNullOrWhiteSpace(parsed.AgentId))
        {
            throw new InvalidOperationException("Uploaded agent file must declare an id in frontmatter or use a named file.");
        }

        var destinationPath = GetAgentFilePath(parsed.AgentId);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.WriteAllText(destinationPath, content.TrimEnd() + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return Find(parsed.AgentId)
            ?? throw new InvalidOperationException($"Uploaded agent '{parsed.AgentId}', but it could not be reloaded.");
    }

    private ManagedAgentDocument ToDocument(AgentProfile profile)
    {
        var effectiveFilePath = GetAgentFilePath(profile.AgentId);
        if (File.Exists(effectiveFilePath))
        {
            return new ManagedAgentDocument(
                profile,
                File.ReadAllText(effectiveFilePath, Encoding.UTF8),
                effectiveFilePath,
                effectiveFilePath);
        }

        return new ManagedAgentDocument(
            profile,
            AgentMarkdownSerializer.Serialize(profile),
            effectiveFilePath,
            null);
    }

    private string GetAgentFilePath(string agentId) =>
        Path.Combine(_paths.AgentsDirectory, NormalizeAgentId(agentId), "AGENT.md");

    private static string DeriveDirectoryId(string fileName)
    {
        var normalized = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (string.Equals(normalized, "AGENT", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(normalized))
        {
            return "uploaded-agent";
        }

        return normalized;
    }

    private static string NormalizeAgentId(string agentId)
    {
        var trimmed = agentId.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Agent id is required.");
        }

        foreach (var character in trimmed)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            {
                continue;
            }

            throw new InvalidOperationException(InvalidAgentIdMessage);
        }

        return trimmed;
    }
}

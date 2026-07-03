namespace Agentwerke.Agents;

/// <summary>
/// Resolves agent profiles by id. Default implementation merges file-based
/// agents (AGENT.md) over the built-in <see cref="AgentRegistry"/> defaults.
/// </summary>
public interface IAgentRegistry
{
    AgentProfile? Find(string agentId);

    IReadOnlyList<AgentProfile> All();
}

/// <summary>
/// Agent registry seeded with the built-in profiles and overlaid with any
/// profiles loaded from AGENT.md files (file agents win on id collision).
/// </summary>
public sealed class FileAgentRegistry : IAgentRegistry
{
    private readonly Func<IReadOnlyList<AgentProfile>> _fileProfileLoader;

    public FileAgentRegistry(IReadOnlyList<AgentProfile> fileProfiles)
        : this(() => fileProfiles)
    {
    }

    public FileAgentRegistry(string agentsDirectory)
        : this(() => MarkdownAgentLoader.LoadFromDirectory(agentsDirectory))
    {
    }

    public FileAgentRegistry(AgentRegistryPaths paths)
        : this(() => LoadFromDirectories(paths))
    {
    }

    private FileAgentRegistry(Func<IReadOnlyList<AgentProfile>> fileProfileLoader)
    {
        _fileProfileLoader = fileProfileLoader;
    }

    public AgentProfile? Find(string agentId) =>
        BuildCatalog().TryGetValue(agentId, out var profile) ? profile : null;

    public IReadOnlyList<AgentProfile> All() =>
        BuildCatalog().Values
            .OrderBy(static p => p.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private Dictionary<string, AgentProfile> BuildCatalog()
    {
        var byId = new Dictionary<string, AgentProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in AgentRegistry.All())
        {
            byId[profile.AgentId] = profile;
        }

        foreach (var profile in _fileProfileLoader())
        {
            byId[profile.AgentId] = profile;
        }

        return byId;
    }

    private static IReadOnlyList<AgentProfile> LoadFromDirectories(AgentRegistryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var profiles = new List<AgentProfile>();
        var directories = new[] { paths.AgentsDirectory, paths.WritableAgentsDirectory }
            .Where(static directory => !string.IsNullOrWhiteSpace(directory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            profiles.AddRange(MarkdownAgentLoader.LoadFromDirectory(directory));
        }

        return profiles;
    }
}

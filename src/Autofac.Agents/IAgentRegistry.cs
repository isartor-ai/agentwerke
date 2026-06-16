namespace Autofac.Agents;

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
    private readonly Dictionary<string, AgentProfile> _byId;

    public FileAgentRegistry(IReadOnlyList<AgentProfile> fileProfiles)
    {
        _byId = new Dictionary<string, AgentProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in AgentRegistry.All())
        {
            _byId[profile.AgentId] = profile;
        }

        foreach (var profile in fileProfiles)
        {
            _byId[profile.AgentId] = profile;
        }
    }

    public AgentProfile? Find(string agentId) =>
        _byId.TryGetValue(agentId, out var profile) ? profile : null;

    public IReadOnlyList<AgentProfile> All() =>
        _byId.Values
            .OrderBy(static p => p.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

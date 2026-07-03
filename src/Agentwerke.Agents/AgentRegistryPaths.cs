using Agentwerke.Agents.Skills;
using Microsoft.Extensions.Configuration;

namespace Agentwerke.Agents;

public sealed record AgentRegistryPaths(
    string AgentsDirectory,
    string SkillsDirectory)
{
    public string WritableAgentsDirectory { get; init; } = AgentsDirectory;

    public static AgentRegistryPaths Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var agentOptions = new AgentOptions();
        configuration.GetSection(AgentOptions.Section).Bind(agentOptions);

        var skillOptions = new SkillOptions();
        configuration.GetSection(SkillOptions.Section).Bind(skillOptions);

        var agentsDirectory = ResolveDirectory(agentOptions.AgentsDirectory);
        if (string.IsNullOrWhiteSpace(agentsDirectory))
        {
            agentsDirectory = Path.GetFullPath("agents");
        }

        var writableAgentsDirectory = ResolveDirectory(agentOptions.WritableAgentsDirectory);
        if (string.IsNullOrWhiteSpace(writableAgentsDirectory))
        {
            writableAgentsDirectory = agentsDirectory;
        }

        var skillsDirectory = ResolveDirectory(skillOptions.SkillsDirectory);
        if (string.IsNullOrWhiteSpace(skillsDirectory))
        {
            var fallback = Path.GetFullPath(Path.Combine(".github", "skills"));
            if (Directory.Exists(fallback))
            {
                skillsDirectory = fallback;
            }
        }

        return new AgentRegistryPaths(agentsDirectory, skillsDirectory)
        {
            WritableAgentsDirectory = writableAgentsDirectory
        };
    }

    private static string ResolveDirectory(string? configuredDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.GetFullPath(configuredDirectory);
    }
}

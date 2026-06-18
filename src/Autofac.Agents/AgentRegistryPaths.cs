using Autofac.Agents.Skills;
using Microsoft.Extensions.Configuration;

namespace Autofac.Agents;

public sealed record AgentRegistryPaths(
    string AgentsDirectory,
    string SkillsDirectory)
{
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

        var skillsDirectory = ResolveDirectory(skillOptions.SkillsDirectory);
        if (string.IsNullOrWhiteSpace(skillsDirectory))
        {
            var fallback = Path.GetFullPath(Path.Combine(".github", "skills"));
            if (Directory.Exists(fallback))
            {
                skillsDirectory = fallback;
            }
        }

        return new AgentRegistryPaths(agentsDirectory, skillsDirectory);
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

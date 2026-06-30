namespace Autofac.Agents;

public sealed class AgentOptions
{
    public const string Section = "Agents:Registry";

    /// <summary>
    /// Absolute or relative path to a directory containing agent subdirectories.
    /// Each subdirectory must contain an AGENT.md file. File agents override
    /// built-in agents of the same id. Defaults to empty (no file agents loaded).
    /// </summary>
    public string AgentsDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Absolute or relative path where UI/API-authored agent overlays are written.
    /// Defaults to <see cref="AgentsDirectory"/> for backward compatibility. Set
    /// this separately when <see cref="AgentsDirectory"/> is mounted read-only.
    /// </summary>
    public string WritableAgentsDirectory { get; set; } = string.Empty;
}

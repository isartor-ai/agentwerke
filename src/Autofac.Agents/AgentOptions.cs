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
}

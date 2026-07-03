namespace Agentwerke.Agents.Skills;

public sealed class SkillOptions
{
    public const string Section = "Agents:Skills";

    /// <summary>
    /// Absolute or relative path to the directory that contains skill subdirectories.
    /// Each subdirectory must contain a SKILL.md file.
    /// Defaults to empty string (no skills loaded).
    /// </summary>
    public string SkillsDirectory { get; set; } = string.Empty;
}

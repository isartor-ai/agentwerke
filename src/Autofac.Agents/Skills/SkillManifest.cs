namespace Autofac.Agents.Skills;

/// <summary>
/// A skill loaded from a Markdown file with YAML frontmatter.
/// Convention: each subdirectory under the skills root contains one SKILL.md.
/// </summary>
public sealed record SkillManifest(
    /// <summary>Directory name — stable identifier used in agent bindings.</summary>
    string SkillId,
    /// <summary>Human-readable name from frontmatter 'name:' field.</summary>
    string Name,
    /// <summary>Short description from frontmatter 'description:' field.</summary>
    string Description,
    /// <summary>Optional semantic version declared in frontmatter.</summary>
    string? Version,
    /// <summary>Rules that describe when the skill should be invoked.</summary>
    IReadOnlyList<string> InvocationRules,
    /// <summary>Relative file paths the skill expects to have available.</summary>
    IReadOnlyList<string> RequiredFiles,
    /// <summary>Optional tools the skill can make use of.</summary>
    IReadOnlyList<string> OptionalTools,
    /// <summary>Full Markdown body (without frontmatter delimiters).</summary>
    string Content,
    /// <summary>SHA-256 hex digest of the raw file bytes — used for audit/traceability.</summary>
    string Fingerprint,
    /// <summary>Absolute path to the source file.</summary>
    string FilePath);

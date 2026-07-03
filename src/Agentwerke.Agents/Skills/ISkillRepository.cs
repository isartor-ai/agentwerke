namespace Agentwerke.Agents.Skills;

public interface ISkillRepository
{
    /// <summary>Find by directory-name skill ID (e.g. "spec-driven-development").</summary>
    SkillManifest? FindById(string skillId);

    /// <summary>Find by the frontmatter 'name:' value.</summary>
    SkillManifest? FindByName(string name);

    SkillManifest? FindByReference(string skillIdOrName);

    IReadOnlyList<SkillManifest> All();
}

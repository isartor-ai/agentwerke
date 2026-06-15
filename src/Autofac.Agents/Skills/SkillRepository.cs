namespace Autofac.Agents.Skills;

public sealed class SkillRepository : ISkillRepository
{
    private readonly IReadOnlyDictionary<string, SkillManifest> _byId;
    private readonly IReadOnlyDictionary<string, SkillManifest> _byName;
    private readonly IReadOnlyList<SkillManifest> _all;

    public SkillRepository(IReadOnlyList<SkillManifest> manifests)
    {
        _all = manifests;
        _byId = manifests.ToDictionary(m => m.SkillId, StringComparer.OrdinalIgnoreCase);
        _byName = manifests
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public SkillManifest? FindById(string skillId) =>
        _byId.TryGetValue(skillId, out var m) ? m : null;

    public SkillManifest? FindByName(string name) =>
        _byName.TryGetValue(name, out var m) ? m : null;

    public SkillManifest? FindByReference(string skillIdOrName) =>
        FindById(skillIdOrName) ?? FindByName(skillIdOrName);

    public IReadOnlyList<SkillManifest> All() => _all;
}

namespace Agentwerke.Agents.Skills;

public sealed class SkillRepository : ISkillRepository
{
    private readonly Func<IReadOnlyList<SkillManifest>> _manifestLoader;

    public SkillRepository(IReadOnlyList<SkillManifest> manifests)
        : this(() => manifests)
    {
    }

    public SkillRepository(string skillsDirectory)
        : this(() => MarkdownSkillLoader.LoadFromDirectory(skillsDirectory))
    {
    }

    private SkillRepository(Func<IReadOnlyList<SkillManifest>> manifestLoader)
    {
        _manifestLoader = manifestLoader;
    }

    public SkillManifest? FindById(string skillId) =>
        CreateSnapshot().ById.TryGetValue(skillId, out var manifest) ? manifest : null;

    public SkillManifest? FindByName(string name) =>
        CreateSnapshot().ByName.TryGetValue(name, out var manifest) ? manifest : null;

    public SkillManifest? FindByReference(string skillIdOrName) =>
        FindById(skillIdOrName) ?? FindByName(skillIdOrName);

    public IReadOnlyList<SkillManifest> All() => CreateSnapshot().All;

    private SkillRepositorySnapshot CreateSnapshot()
    {
        var manifests = _manifestLoader();
        var byId = manifests.ToDictionary(manifest => manifest.SkillId, StringComparer.OrdinalIgnoreCase);
        var byName = manifests
            .GroupBy(manifest => manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return new SkillRepositorySnapshot(manifests, byId, byName);
    }

    private sealed record SkillRepositorySnapshot(
        IReadOnlyList<SkillManifest> All,
        IReadOnlyDictionary<string, SkillManifest> ById,
        IReadOnlyDictionary<string, SkillManifest> ByName);
}

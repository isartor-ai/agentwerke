using Autofac.Agents.Skills;

namespace Autofac.Agents.Tests;

public sealed class MarkdownSkillLoaderTests
{
    private const string SampleSkill = """
        ---
        name: test-driven-development
        description: Creates tests before code.
        version: 1.2.3
        invocationRules:
          - write tests first
          - keep tests deterministic
        requiredFiles:
          - templates/test-plan.md
        optionalTools: [dotnet test, rg]
        ---
        # Test-Driven Development

        Write the test first, then make it pass.
        """;

    [Fact]
    public void Parse_WithValidFrontmatter_ExtractsNameAndDescription()
    {
        var skillDir = CreateSkillDirectory();
        File.WriteAllText(Path.Combine(skillDir, "templates", "test-plan.md"), "template");
        var manifest = MarkdownSkillLoader.Parse("test-skill", Path.Combine(skillDir, "SKILL.md"), SampleSkill);

        Assert.NotNull(manifest);
        Assert.Equal("test-skill", manifest.SkillId);
        Assert.Equal("test-driven-development", manifest.Name);
        Assert.Equal("Creates tests before code.", manifest.Description);
        Assert.Equal("1.2.3", manifest.Version);
        Assert.Equal(["write tests first", "keep tests deterministic"], manifest.InvocationRules);
        Assert.Equal(["templates/test-plan.md"], manifest.RequiredFiles);
        Assert.Equal(["dotnet test", "rg"], manifest.OptionalTools);
    }

    [Fact]
    public void Parse_WithValidFrontmatter_ExtractsBodyWithoutDelimiters()
    {
        var skillDir = CreateSkillDirectory();
        File.WriteAllText(Path.Combine(skillDir, "templates", "test-plan.md"), "template");
        var manifest = MarkdownSkillLoader.Parse("test-skill", Path.Combine(skillDir, "SKILL.md"), SampleSkill);

        Assert.NotNull(manifest);
        Assert.Contains("# Test-Driven Development", manifest.Content);
        Assert.DoesNotContain("---", manifest.Content);
    }

    [Fact]
    public void Parse_ComputesSha256Fingerprint()
    {
        var skillDir = CreateSkillDirectory();
        File.WriteAllText(Path.Combine(skillDir, "templates", "test-plan.md"), "template");
        var manifest = MarkdownSkillLoader.Parse("test-skill", Path.Combine(skillDir, "SKILL.md"), SampleSkill);

        Assert.NotNull(manifest);
        Assert.Equal(64, manifest.Fingerprint.Length); // SHA-256 hex = 64 chars
        Assert.Matches("^[0-9a-f]+$", manifest.Fingerprint);
    }

    [Fact]
    public void Parse_FingerprintIsDeterministic()
    {
        var skillDir = CreateSkillDirectory();
        File.WriteAllText(Path.Combine(skillDir, "templates", "test-plan.md"), "template");
        var filePath = Path.Combine(skillDir, "SKILL.md");
        var m1 = MarkdownSkillLoader.Parse("s", filePath, SampleSkill);
        var m2 = MarkdownSkillLoader.Parse("s", filePath, SampleSkill);

        Assert.Equal(m1!.Fingerprint, m2!.Fingerprint);
    }

    [Fact]
    public void Parse_DifferentContent_ProducesDifferentFingerprint()
    {
        var skillDir = CreateSkillDirectory();
        File.WriteAllText(Path.Combine(skillDir, "templates", "test-plan.md"), "template");
        var filePath = Path.Combine(skillDir, "SKILL.md");
        var m1 = MarkdownSkillLoader.Parse("s", filePath, SampleSkill);
        var m2 = MarkdownSkillLoader.Parse("s", filePath, SampleSkill + " changed");

        Assert.NotEqual(m1!.Fingerprint, m2!.Fingerprint);
    }

    [Fact]
    public void Parse_NoFrontmatter_UsesSkillIdAsNameAndFullContentAsBody()
    {
        const string raw = "# Just Markdown\nNo frontmatter here.";
        var manifest = MarkdownSkillLoader.Parse("my-skill", "/f", raw);

        Assert.NotNull(manifest);
        Assert.Equal("my-skill", manifest.Name);
        Assert.Contains("Just Markdown", manifest.Content);
    }

    [Fact]
    public void Parse_MissingNameField_FallsBackToSkillId()
    {
        var skillDir = CreateSkillDirectory();
        const string raw = """
            ---
            description: Only a description.
            ---
            Body content.
            """;
        var manifest = MarkdownSkillLoader.Parse("fallback-id", Path.Combine(skillDir, "SKILL.md"), raw);

        Assert.NotNull(manifest);
        Assert.Equal("fallback-id", manifest.Name);
        Assert.Equal("Only a description.", manifest.Description);
    }

    [Fact]
    public void SkillRepository_FindById_ReturnsByDirectoryName()
    {
        var skillDir = CreateSkillDirectory();
        File.WriteAllText(Path.Combine(skillDir, "templates", "test-plan.md"), "template");
        var manifests = new[]
        {
            MarkdownSkillLoader.Parse("skill-a", Path.Combine(skillDir, "skill-a.md"), SampleSkill)!,
            MarkdownSkillLoader.Parse("skill-b", Path.Combine(skillDir, "skill-b.md"), "---\nname: b\n---\nBody")!,
        };
        var repo = new SkillRepository(manifests);

        Assert.NotNull(repo.FindById("skill-a"));
        Assert.NotNull(repo.FindById("SKILL-A")); // case-insensitive
        Assert.Null(repo.FindById("skill-z"));
    }

    [Fact]
    public void SkillRepository_FindByName_ReturnsByFrontmatterName()
    {
        var skillDir = CreateSkillDirectory();
        File.WriteAllText(Path.Combine(skillDir, "templates", "test-plan.md"), "template");
        var manifests = new[]
        {
            MarkdownSkillLoader.Parse("skill-a", Path.Combine(skillDir, "SKILL.md"), SampleSkill)!
        };
        var repo = new SkillRepository(manifests);

        var found = repo.FindByName("test-driven-development");
        Assert.NotNull(found);
        Assert.Equal("skill-a", found.SkillId);
    }

    [Fact]
    public void SkillRepository_FindByReference_ResolvesIdOrName()
    {
        var skillDir = CreateSkillDirectory();
        File.WriteAllText(Path.Combine(skillDir, "templates", "test-plan.md"), "template");
        var manifest = MarkdownSkillLoader.Parse("skill-a", Path.Combine(skillDir, "SKILL.md"), SampleSkill)!;
        var repo = new SkillRepository([manifest]);

        Assert.Same(manifest, repo.FindByReference("skill-a"));
        Assert.Same(manifest, repo.FindByReference("test-driven-development"));
    }

    [Fact]
    public void Parse_WhenRequiredFileMissing_ThrowsActionableError()
    {
        var skillDir = CreateSkillDirectory();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MarkdownSkillLoader.Parse("missing-file-skill", Path.Combine(skillDir, "SKILL.md"), SampleSkill));

        Assert.Contains("requires file", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("templates/test-plan.md", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenVersionIsInvalid_ThrowsActionableError()
    {
        var skillDir = CreateSkillDirectory();
        var invalidRaw = """
            ---
            name: invalid-version
            version: version with spaces
            ---
            Body.
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MarkdownSkillLoader.Parse("invalid-version", Path.Combine(skillDir, "SKILL.md"), invalidRaw));

        Assert.Contains("invalid version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateSkillDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "templates"));
        return directory;
    }
}

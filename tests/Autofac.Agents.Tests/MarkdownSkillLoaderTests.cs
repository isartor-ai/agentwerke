using Autofac.Agents.Skills;

namespace Autofac.Agents.Tests;

public sealed class MarkdownSkillLoaderTests
{
    private const string SampleSkill = """
        ---
        name: test-driven-development
        description: Creates tests before code.
        ---
        # Test-Driven Development

        Write the test first, then make it pass.
        """;

    [Fact]
    public void Parse_WithValidFrontmatter_ExtractsNameAndDescription()
    {
        var manifest = MarkdownSkillLoader.Parse("test-skill", "/skills/test/SKILL.md", SampleSkill);

        Assert.NotNull(manifest);
        Assert.Equal("test-skill", manifest.SkillId);
        Assert.Equal("test-driven-development", manifest.Name);
        Assert.Equal("Creates tests before code.", manifest.Description);
    }

    [Fact]
    public void Parse_WithValidFrontmatter_ExtractsBodyWithoutDelimiters()
    {
        var manifest = MarkdownSkillLoader.Parse("test-skill", "/skills/test/SKILL.md", SampleSkill);

        Assert.NotNull(manifest);
        Assert.Contains("# Test-Driven Development", manifest.Content);
        Assert.DoesNotContain("---", manifest.Content);
    }

    [Fact]
    public void Parse_ComputesSha256Fingerprint()
    {
        var manifest = MarkdownSkillLoader.Parse("test-skill", "/skills/test/SKILL.md", SampleSkill);

        Assert.NotNull(manifest);
        Assert.Equal(64, manifest.Fingerprint.Length); // SHA-256 hex = 64 chars
        Assert.Matches("^[0-9a-f]+$", manifest.Fingerprint);
    }

    [Fact]
    public void Parse_FingerprintIsDeterministic()
    {
        var m1 = MarkdownSkillLoader.Parse("s", "/f", SampleSkill);
        var m2 = MarkdownSkillLoader.Parse("s", "/f", SampleSkill);

        Assert.Equal(m1!.Fingerprint, m2!.Fingerprint);
    }

    [Fact]
    public void Parse_DifferentContent_ProducesDifferentFingerprint()
    {
        var m1 = MarkdownSkillLoader.Parse("s", "/f", SampleSkill);
        var m2 = MarkdownSkillLoader.Parse("s", "/f", SampleSkill + " changed");

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
        const string raw = """
            ---
            description: Only a description.
            ---
            Body content.
            """;
        var manifest = MarkdownSkillLoader.Parse("fallback-id", "/f", raw);

        Assert.NotNull(manifest);
        Assert.Equal("fallback-id", manifest.Name);
        Assert.Equal("Only a description.", manifest.Description);
    }

    [Fact]
    public void SkillRepository_FindById_ReturnsByDirectoryName()
    {
        var manifests = new[]
        {
            MarkdownSkillLoader.Parse("skill-a", "/a/SKILL.md", SampleSkill)!,
            MarkdownSkillLoader.Parse("skill-b", "/b/SKILL.md", "---\nname: b\n---\nBody")!,
        };
        var repo = new SkillRepository(manifests);

        Assert.NotNull(repo.FindById("skill-a"));
        Assert.NotNull(repo.FindById("SKILL-A")); // case-insensitive
        Assert.Null(repo.FindById("skill-z"));
    }

    [Fact]
    public void SkillRepository_FindByName_ReturnsByFrontmatterName()
    {
        var manifests = new[]
        {
            MarkdownSkillLoader.Parse("skill-a", "/a/SKILL.md", SampleSkill)!
        };
        var repo = new SkillRepository(manifests);

        var found = repo.FindByName("test-driven-development");
        Assert.NotNull(found);
        Assert.Equal("skill-a", found.SkillId);
    }
}

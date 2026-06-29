using Autofac.Agents;

namespace Autofac.Agents.Tests;

public sealed class MarkdownAgentLoaderTests
{
    [Fact]
    public void Parse_ReadsFrontmatterAndBody()
    {
        const string md = """
            ---
            name: Business Analyst
            category: analysis
            description: Turns an idea into a spec.
            runner: claude-code
            model: claude-opus-4-8
            dockerImage: autofac/agent-base
            network: bridge
            skills: [requirement-design, spec-authoring]
            tools: [web_search, web_fetch]
            deniedTools: [sandbox.execute]
            supportedActions:
              - requirement-design
              - design-requirements
            supportedEnvironments: [all]
            supportedPolicyTags: [requirement-design, doc-generation]
            secrets: [ANTHROPIC_API_KEY]
            sandboxProfiles: [repo-read]
            ---

            You are a senior Business Analyst. Read {{input.body}} and write a spec.
            """;

        var profile = MarkdownAgentLoader.Parse("business-analyst", md);

        Assert.Equal("business-analyst", profile.AgentId);
        Assert.Equal("Business Analyst", profile.Name);
        Assert.Equal("analysis", profile.Category);
        Assert.Equal("claude-code", profile.Runner);
        Assert.Equal("claude-opus-4-8", profile.Model);
        Assert.Equal("autofac/agent-base", profile.DockerImage);
        Assert.Equal("bridge", profile.Network);
        Assert.Equal(["web_search", "web_fetch"], profile.Tools);
        Assert.Equal(["sandbox.execute"], profile.DeniedTools);
        Assert.Equal(["ANTHROPIC_API_KEY"], profile.Secrets);
        Assert.Equal(["requirement-design", "design-requirements"], profile.SupportedActions);
        Assert.Equal(["all"], profile.SupportedEnvironments);
        Assert.Equal(["repo-read"], profile.SandboxProfiles);
        Assert.Equal("file", profile.Source);
        Assert.NotNull(profile.SystemPrompt);
        Assert.Contains("senior Business Analyst", profile.SystemPrompt);
        Assert.NotNull(profile.Fingerprint);

        // Declared skills become refs carrying the agent's supported actions,
        // with no manifest binding (so runs don't fail on a missing manifest).
        Assert.Equal(2, profile.Skills.Count);
        Assert.All(profile.Skills, s => Assert.Null(s.SkillManifestId));
        Assert.Contains(profile.Skills, s => s.SkillId == "requirement-design");
    }

    [Fact]
    public void Parse_DefaultsIdAndRunnerWhenAbsent()
    {
        var profile = MarkdownAgentLoader.Parse("my-agent", "no frontmatter here");

        Assert.Equal("my-agent", profile.AgentId);
        Assert.Equal("my-agent", profile.Name);
        Assert.Equal("agent-model", profile.Runner);
        Assert.Equal("none", profile.Network);
    }

    [Fact]
    public void LoadFromDirectory_ReadsAgentSubdirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agents_{Guid.NewGuid():N}");
        var agentDir = Path.Combine(root, "tester");
        Directory.CreateDirectory(agentDir);
        try
        {
            File.WriteAllText(Path.Combine(agentDir, "AGENT.md"), """
                ---
                name: Tester
                category: quality
                ---
                Run the tests.
                """);
            // A subdirectory without AGENT.md is ignored.
            Directory.CreateDirectory(Path.Combine(root, "empty"));

            var profiles = MarkdownAgentLoader.LoadFromDirectory(root);

            var tester = Assert.Single(profiles);
            Assert.Equal("tester", tester.AgentId);
            Assert.Equal("Tester", tester.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FileAgentRegistry_OverlaysFileAgentsOverBuiltins()
    {
        var custom = MarkdownAgentLoader.Parse("business-analyst", """
            ---
            name: Custom BA
            ---
            override
            """);

        var registry = new FileAgentRegistry([custom]);

        // File agent overrides the built-in of the same id.
        Assert.Equal("Custom BA", registry.Find("business-analyst")!.Name);
        // Built-ins remain available.
        Assert.NotNull(registry.Find("github-agent"));
        // Unknown id resolves to null.
        Assert.Null(registry.Find("does-not-exist"));
    }

    [Fact]
    public void Parse_SkillBindingsJson_PreservesManifestAndSupportedActions()
    {
        const string md = """
            ---
            id: github-agent
            name: GitHub Agent
            supportedActions:
              - github.create_branch
              - github.create_pull_request
            skillBindings:
              - {"skillId":"github-branching","name":"GitHub Branching","description":"Create branches","supportedActions":["github.create_branch"],"skillManifestId":"git-workflow-and-versioning"}
              - {"skillId":"github-pr","name":"GitHub Pull Request","description":"Create pull requests","supportedActions":["github.create_pull_request"],"skillManifestId":"git-workflow-and-versioning"}
            ---
            Use GitHub.
            """;

        var profile = MarkdownAgentLoader.Parse("github-agent", md);

        Assert.Equal(2, profile.Skills.Count);
        Assert.Equal("git-workflow-and-versioning", profile.Skills[0].SkillManifestId);
        Assert.Equal(["github.create_branch"], profile.Skills[0].SupportedActions);
        Assert.Equal("GitHub Pull Request", profile.Skills[1].Name);
    }

    [Fact]
    public void FileAgentRegistry_DirectoryBackedRegistry_ReloadsWrittenFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agents_{Guid.NewGuid():N}");
        var agentDir = Path.Combine(root, "reloadable");
        Directory.CreateDirectory(agentDir);

        try
        {
            File.WriteAllText(Path.Combine(agentDir, "AGENT.md"), """
                ---
                name: Before
                category: quality
                ---
                Original prompt.
                """);

            var registry = new FileAgentRegistry(root);
            Assert.Equal("Before", registry.Find("reloadable")!.Name);

            File.WriteAllText(Path.Combine(agentDir, "AGENT.md"), """
                ---
                name: After
                category: quality
                ---
                Updated prompt.
                """);

            Assert.Equal("After", registry.Find("reloadable")!.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("implementation-engineer", "sandbox.file_edit", "sandbox.git", "github.create_pull_request")]
    [InlineData("senior-code-reviewer", "sandbox.git", "github.post_review")]
    [InlineData("tester", "sandbox.git", "sandbox.run_tests")]
    public void LoadFromDirectory_ParsesSdlcCodeAgentProfiles(string agentId, params string[] expectedTools)
    {
        var profiles = MarkdownAgentLoader.LoadFromDirectory(FindRepositoryAgentsDirectory());

        var profile = Assert.Single(profiles, p => p.AgentId == agentId);
        Assert.Equal("claude-code", profile.Runner);
        Assert.NotEmpty(profile.SandboxProfiles);
        foreach (var expectedTool in expectedTools)
        {
            Assert.Contains(expectedTool, profile.Tools);
        }
        Assert.False(string.IsNullOrWhiteSpace(profile.SystemPrompt));
    }

    [Fact]
    public void LoadFromDirectory_ParsesFirstRunSampleAgentProfile()
    {
        var profiles = MarkdownAgentLoader.LoadFromDirectory(FindRepositoryAgentsDirectory());

        var profile = Assert.Single(profiles, p => p.AgentId == "first-run-engineer");
        Assert.Equal("agent-model", profile.Runner);
        Assert.Equal("onboarding", profile.Category);
        Assert.Contains("first-run.implement", profile.SupportedActions);
        Assert.Contains("quickstart", profile.SupportedEnvironments);

        var skill = Assert.Single(profile.Skills);
        Assert.Equal("first-run-sample", skill.SkillId);
        Assert.Equal("first-run-sample", skill.SkillManifestId);
        Assert.Equal(["first-run.implement"], skill.SupportedActions);
    }

    private static string FindRepositoryAgentsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Autofac.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (Autofac.sln) from the test output directory.");
        }

        return Path.Combine(directory.FullName, "agents");
    }
}

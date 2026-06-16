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
}

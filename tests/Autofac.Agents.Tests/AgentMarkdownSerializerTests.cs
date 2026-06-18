using Autofac.Agents;

namespace Autofac.Agents.Tests;

public sealed class AgentMarkdownSerializerTests
{
    [Fact]
    public void Serialize_WritesFrontmatterAndBody()
    {
        var profile = new AgentProfile
        {
            AgentId = "business-analyst",
            Name = "Business Analyst: Core",
            Description = "Turns an idea into a spec.",
            Category = "analysis",
            Runner = "claude-code",
            Model = "claude-opus-4-8",
            DockerImage = "autofac/agent-base",
            Network = "bridge",
            Skills =
            [
                new AgentSkillRef(
                    "requirement-design",
                    "Requirement Design",
                    "Elicit and structure requirements",
                    ["design-requirements"]),
            ],
            Tools = ["web_search", "web_fetch"],
            DeniedTools = ["sandbox.execute"],
            Secrets = ["ANTHROPIC_API_KEY"],
            SupportedActions = ["requirement-design", "design-requirements"],
            SupportedEnvironments = ["all"],
            SupportedPolicyTags = ["requirement-design", "doc-generation"],
            SystemPrompt = "You are a senior Business Analyst.\nRead {{input.body}} and write a spec.",
        };

        var markdown = AgentMarkdownSerializer.Serialize(profile);

        var expected = string.Join(Environment.NewLine,
            [
                "---",
                "id: business-analyst",
                "name: \"Business Analyst: Core\"",
                "description: Turns an idea into a spec.",
                "category: analysis",
                "runner: claude-code",
                "model: claude-opus-4-8",
                "dockerImage: autofac/agent-base",
                "network: bridge",
                "skills:",
                "  - requirement-design",
                "skillBindings:",
                "  - {\"skillId\":\"requirement-design\",\"name\":\"Requirement Design\",\"description\":\"Elicit and structure requirements\",\"supportedActions\":[\"design-requirements\"],\"skillManifestId\":null}",
                "tools:",
                "  - web_search",
                "  - web_fetch",
                "deniedTools:",
                "  - sandbox.execute",
                "secrets:",
                "  - ANTHROPIC_API_KEY",
                "supportedActions:",
                "  - requirement-design",
                "  - design-requirements",
                "supportedEnvironments:",
                "  - all",
                "supportedPolicyTags:",
                "  - requirement-design",
                "  - doc-generation",
                "---",
                string.Empty,
                "You are a senior Business Analyst.",
                "Read {{input.body}} and write a spec.",
            ]) + Environment.NewLine;

        Assert.Equal(expected, markdown);
    }

    [Fact]
    public void Serialize_WritesStructuredSkillBindings()
    {
        var profile = new AgentProfile
        {
            AgentId = "github-agent",
            Name = "GitHub Agent",
            Description = "Creates branches and pull requests.",
            Category = "integration",
            Runner = "agent-model",
            Skills =
            [
                new AgentSkillRef(
                    "github-branching",
                    "GitHub Branching",
                    "Create deterministic Autofac branches",
                    ["github.create_branch"],
                    SkillManifestId: "git-workflow-and-versioning"),
                new AgentSkillRef(
                    "github-pr",
                    "GitHub Pull Request",
                    "Open draft pull requests",
                    ["github.create_pull_request", "github.create_pr"],
                    SkillManifestId: "git-workflow-and-versioning")
            ],
            Tools = ["github.create_branch", "github.create_pull_request"],
            SupportedActions = ["github.create_branch", "github.create_pull_request", "github.create_pr"],
            SupportedEnvironments = ["github"],
            SupportedPolicyTags = ["repo-change", "pull-request"],
            SystemPrompt = "Use the GitHub connector safely.",
        };

        var markdown = AgentMarkdownSerializer.Serialize(profile);

        Assert.Contains("skills:", markdown, StringComparison.Ordinal);
        Assert.Contains("skillBindings:", markdown, StringComparison.Ordinal);
        Assert.Contains("\"skillId\":\"github-branching\"", markdown, StringComparison.Ordinal);
        Assert.Contains("\"supportedActions\":[\"github.create_pull_request\",\"github.create_pr\"]", markdown, StringComparison.Ordinal);
        Assert.Contains("Use the GitHub connector safely.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_EmptyDescription_RoundTripsAsScalar()
    {
        var profile = new AgentProfile
        {
            AgentId = "ops-agent",
            Name = "Ops Agent",
            Description = string.Empty,
            Category = "operations",
            Runner = "agent-model",
        };

        var markdown = AgentMarkdownSerializer.Serialize(profile);
        var roundTripped = MarkdownAgentLoader.Parse("ops-agent", markdown);

        Assert.Contains("description: \"\"", markdown, StringComparison.Ordinal);
        Assert.Equal(string.Empty, roundTripped.Description);
    }
}

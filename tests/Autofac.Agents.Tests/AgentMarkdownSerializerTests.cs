using Autofac.Agents;

namespace Autofac.Agents.Tests;

public sealed class AgentMarkdownSerializerTests
{
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

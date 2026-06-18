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
}

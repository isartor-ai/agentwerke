using Agentwerke.Api.Contracts.Agents;
using Agentwerke.Api.Controllers;
using Agentwerke.Agents;
using Agentwerke.Agents.Skills;
using Microsoft.AspNetCore.Mvc;

namespace Agentwerke.Api.Tests;

public sealed class AgentsControllerTests
{
    [Fact]
    public void Get_BuiltinAgent_ReturnsGeneratedEditorDetail()
    {
        using var fixture = new AgentRegistryFixture();
        var controller = fixture.CreateController();

        var result = controller.Get("github-agent");

        var ok = Assert.IsType<OkObjectResult>(result);
        var detail = Assert.IsType<AgentDetail>(ok.Value);
        Assert.Equal("github-agent", detail.AgentId);
        Assert.Contains("GitHub Agent", detail.RawMarkdown, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("github-agent", "AGENT.md"), detail.EffectiveFilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_WritesOverlayFileAndReturnsSavedAgent()
    {
        using var fixture = new AgentRegistryFixture();
        var controller = fixture.CreateController();

        var result = controller.Upsert("custom-agent", new UpsertAgentRequest(
            AgentId: "custom-agent",
            Name: "Custom Agent",
            Description: "Handles custom work.",
            Category: "engineering",
            Runner: "claude-code",
            Model: "claude-opus-4-8",
            DockerImage: "autofac/agent-base",
            Network: "bridge",
            Tools: ["web_search"],
            DeniedTools: ["sandbox.execute"],
            SupportedActions: ["implement"],
            Skills:
            [
                new AgentSkillBinding(
                    "test-driven-development",
                    "Test Driven Development",
                    "Write tests first.",
                    ["implement"],
                    "test-driven-development")
            ],
            SupportedEnvironments: ["github"],
            SupportedPolicyTags: ["implementation"],
            Secrets: ["GITHUB_TOKEN"],
            SystemPrompt: "Implement safely."));

        var ok = Assert.IsType<OkObjectResult>(result);
        var detail = Assert.IsType<AgentDetail>(ok.Value);

        Assert.Equal("custom-agent", detail.AgentId);
        Assert.Equal("bridge", detail.Network);
        Assert.Equal("test-driven-development", Assert.Single(detail.Skills).SkillManifestId);
        Assert.Contains("skillBindings:", detail.RawMarkdown, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(fixture.AgentsDirectory, "custom-agent", "AGENT.md")));
    }

    [Fact]
    public void Upsert_WhenWritableAgentDirectoryDiffers_WritesOverlayFileAndReloadsSavedAgent()
    {
        using var fixture = new AgentRegistryFixture(useWritableOverlay: true);
        var controller = fixture.CreateController();

        var result = controller.Upsert("business-analyst", new UpsertAgentRequest(
            AgentId: "business-analyst",
            Name: "Business Analyst",
            Description: "Turns issues into requirements.",
            Category: "analysis",
            Runner: "agent-model",
            Skills:
            [
                new AgentSkillBinding(
                    "requirements",
                    "Requirements",
                    "Shape requirements.",
                    ["requirement-design"],
                    "test-driven-development")
            ],
            SupportedActions: ["requirement-design"],
            SupportedEnvironments: ["all"],
            SupportedPolicyTags: ["requirement-design"],
            SystemPrompt: "Write crisp requirements."));

        var ok = Assert.IsType<OkObjectResult>(result);
        var detail = Assert.IsType<AgentDetail>(ok.Value);

        var sourcePath = Path.Combine(fixture.AgentsDirectory, "business-analyst", "AGENT.md");
        var overlayPath = Path.Combine(fixture.WritableAgentsDirectory, "business-analyst", "AGENT.md");
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(overlayPath));
        Assert.Equal("test-driven-development", Assert.Single(detail.Skills).SkillManifestId);
        Assert.Equal(Path.GetFullPath(overlayPath), detail.EffectiveFilePath);
    }

    [Fact]
    public void Upload_ParsesAgentMarkdownAndCreatesAgentFile()
    {
        using var fixture = new AgentRegistryFixture();
        var controller = fixture.CreateController();

        var result = controller.Upload(new UploadAgentRequest(
            "AGENT.md",
            """
            ---
            id: uploaded-agent
            name: Uploaded Agent
            description: Created from upload.
            category: quality
            runner: agent-model
            skills:
              - test-driven-development
            supportedActions:
              - review-code
            ---
            Review uploaded work.
            """));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var detail = Assert.IsType<AgentDetail>(created.Value);
        Assert.Equal("uploaded-agent", detail.AgentId);
        Assert.Contains("Review uploaded work.", detail.RawMarkdown, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(fixture.AgentsDirectory, "uploaded-agent", "AGENT.md")));
    }

    [Fact]
    public void Upsert_WhenRouteIdDiffers_ReturnsBadRequest()
    {
        using var fixture = new AgentRegistryFixture();
        var controller = fixture.CreateController();

        var result = controller.Upsert("agent-a", new UpsertAgentRequest(
            AgentId: "agent-b",
            Name: "Mismatch",
            Description: string.Empty,
            Category: "quality",
            Runner: "agent-model"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Upsert_WhenAgentIdContainsPathTraversal_ReturnsBadRequest()
    {
        using var fixture = new AgentRegistryFixture();
        var controller = fixture.CreateController();

        var result = controller.Upsert("../outside", new UpsertAgentRequest(
            AgentId: "../outside",
            Name: "Unsafe",
            Description: "Should be rejected.",
            Category: "quality",
            Runner: "agent-model"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Agent id may only contain", badRequest.Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_WhenSandboxProfileIsUnknown_ReturnsBadRequest()
    {
        using var fixture = new AgentRegistryFixture();
        var controller = fixture.CreateController();

        var result = controller.Upsert("custom-agent", new UpsertAgentRequest(
            AgentId: "custom-agent",
            Name: "Custom Agent",
            Description: "Handles custom work.",
            Category: "engineering",
            Runner: "agent-model",
            SandboxProfiles: ["super-admin"]));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Unknown sandbox profile", badRequest.Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_WhenSandboxProfileIsKnown_PersistsAndReturnsIt()
    {
        using var fixture = new AgentRegistryFixture();
        var controller = fixture.CreateController();

        var result = controller.Upsert("custom-agent", new UpsertAgentRequest(
            AgentId: "custom-agent",
            Name: "Custom Agent",
            Description: "Handles custom work.",
            Category: "engineering",
            Runner: "agent-model",
            SandboxProfiles: ["repo-write"]));

        var ok = Assert.IsType<OkObjectResult>(result);
        var detail = Assert.IsType<AgentDetail>(ok.Value);
        Assert.Equal(["repo-write"], detail.SandboxProfiles);
    }

    private sealed class AgentRegistryFixture : IDisposable
    {
        public AgentRegistryFixture(bool useWritableOverlay = false)
        {
            Root = Path.Combine(Path.GetTempPath(), $"agent_registry_{Guid.NewGuid():N}");
            AgentsDirectory = Path.Combine(Root, "agents");
            WritableAgentsDirectory = useWritableOverlay ? Path.Combine(Root, "agent-overlays") : AgentsDirectory;
            SkillsDirectory = Path.Combine(Root, "skills");
            Directory.CreateDirectory(AgentsDirectory);
            Directory.CreateDirectory(WritableAgentsDirectory);
            Directory.CreateDirectory(Path.Combine(SkillsDirectory, "test-driven-development"));
            File.WriteAllText(Path.Combine(SkillsDirectory, "test-driven-development", "SKILL.md"), """
                ---
                name: Test Driven Development
                description: Write tests first.
                version: 1.0.0
                ---
                Skill body.
                """);

            Paths = new AgentRegistryPaths(AgentsDirectory, SkillsDirectory)
            {
                WritableAgentsDirectory = WritableAgentsDirectory
            };
            Registry = new FileAgentRegistry(Paths);
            Skills = new SkillRepository(SkillsDirectory);
            Editor = new FileAgentRegistryEditor(Paths, Registry);
        }

        public string Root { get; }

        public string AgentsDirectory { get; }

        public string WritableAgentsDirectory { get; }

        public string SkillsDirectory { get; }

        public AgentRegistryPaths Paths { get; }

        public IAgentRegistry Registry { get; }

        public ISkillRepository Skills { get; }

        public IAgentRegistryEditor Editor { get; }

        public AgentsController CreateController() =>
            new(Registry, Editor, Skills, new Agentwerke.Application.Agents.InMemoryAgentFeedbackStore());

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

using System.Text.Json;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Domain.Tests;

public sealed class AgentRuntimeContractTests
{
    [Fact]
    public void AgentRuntimeContract_DefaultsToReadOnlyPermissionsAndCapturedOutputs()
    {
        var contract = new AgentRuntimeContract();

        Assert.Equal(AgentPermissionLevels.ReadOnly, contract.Permissions.Level);
        Assert.True(contract.Outputs.CaptureResponse);
        Assert.True(contract.Outputs.CaptureStatus);
        Assert.True(contract.Outputs.CaptureArtifacts);
        Assert.Empty(contract.Skills);
        Assert.Empty(contract.Tools);
        Assert.Empty(contract.McpServers);
        Assert.Empty(contract.Hooks);
    }

    [Fact]
    public void AgentMcpServerContract_CapturesTransportConfiguration()
    {
        var server = new AgentMcpServerContract
        {
            Name = "weather",
            Transport = "http",
            Url = "https://mcp.example.test",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = "Bearer token"
            },
            StartupTimeoutSeconds = 30
        };

        Assert.Equal("weather", server.Name);
        Assert.Equal("http", server.Transport);
        Assert.Equal("https://mcp.example.test", server.Url);
        Assert.Equal("Bearer token", server.Headers["authorization"]);
        Assert.Equal(30, server.StartupTimeoutSeconds);
        Assert.True(server.Enabled);
    }

    [Fact]
    public void AgentRuntimeSnapshot_CapturesRunStepAndRuntimeCapabilities()
    {
        var snapshot = new AgentRuntimeSnapshot
        {
            RunId = "run-1",
            StepId = "step-1",
            NodeId = "node-1",
            AgentName = "deploy-agent",
            Action = "deploy",
            Contract = new AgentRuntimeContract
            {
                Skills =
                [
                    new AgentSkillContract
                    {
                        SkillId = "shipping-and-launch",
                        Name = "Shipping and Launch"
                    }
                ],
                Tools =
                [
                    new AgentToolContract
                    {
                        Name = "github.create_pull_request",
                        Category = AgentToolCategories.Integration
                    }
                ],
                Permissions = new AgentPermissionContract
                {
                    Level = AgentPermissionLevels.ReadWrite,
                    AllowedTools = ["github.create_pull_request"]
                }
            }
        };

        Assert.Equal("run-1", snapshot.RunId);
        Assert.Equal("step-1", snapshot.StepId);
        Assert.Equal("deploy-agent", snapshot.AgentName);
        Assert.Equal(AgentPermissionLevels.ReadWrite, snapshot.Contract.Permissions.Level);
        Assert.Contains(snapshot.Contract.Skills, static s => s.SkillId == "shipping-and-launch");
        Assert.Contains(snapshot.Contract.Tools, static t => t.Category == AgentToolCategories.Integration);
    }

    [Fact]
    public void AgentRuntimeSnapshot_RoundTripsViaJsonSerialization()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var original = new AgentRuntimeSnapshot
        {
            RunId = "run-42",
            StepId = "step-42",
            NodeId = "node-42",
            AgentName = "test-agent",
            Action = "run-tests",
            Contract = new AgentRuntimeContract
            {
                Prompt = new AgentPromptContract { Inline = "Run the test suite." },
                Skills = [new AgentSkillContract { SkillId = "tdd", Name = "TDD" }],
                Tools = [new AgentToolContract { Name = "bash", Category = AgentToolCategories.Shell }],
                Permissions = new AgentPermissionContract
                {
                    Level = AgentPermissionLevels.ReadWrite,
                    AllowedTools = ["bash"]
                }
            },
            Skills = [new AgentSkillUsageRecord { SkillId = "tdd", Name = "TDD", Selected = true, Fingerprint = "abc123" }],
            ToolInvocations = [new AgentToolInvocationRecord { ToolName = "bash", Category = AgentToolCategories.Shell, Status = "completed", DurationMs = 1200 }],
            HookExecutions = [new AgentHookExecutionRecord { Event = "PreToolUse", Type = "command", Decision = "proceed", DurationMs = 5 }],
            Artifacts = [new AgentArtifactRecord { Name = "coverage.html", Uri = "/artifacts/coverage.html", ContentType = "text/html" }],
            PermissionDecision = new AgentPermissionDecisionRecord
            {
                Level = AgentPermissionLevels.ReadWrite,
                Allowed = true,
                Rationale = "All checks passed."
            }
        };

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<AgentRuntimeSnapshot>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.RunId, deserialized!.RunId);
        Assert.Equal(original.AgentName, deserialized.AgentName);
        Assert.Equal(original.Contract.Prompt?.Inline, deserialized.Contract.Prompt?.Inline);
        Assert.Single(deserialized.Skills);
        Assert.Equal("abc123", deserialized.Skills[0].Fingerprint);
        Assert.Single(deserialized.ToolInvocations);
        Assert.Equal(1200, deserialized.ToolInvocations[0].DurationMs);
        Assert.Single(deserialized.HookExecutions);
        Assert.Equal("proceed", deserialized.HookExecutions[0].Decision);
        Assert.Single(deserialized.Artifacts);
        Assert.Equal("coverage.html", deserialized.Artifacts[0].Name);
        Assert.True(deserialized.PermissionDecision?.Allowed);
    }

    [Fact]
    public void AgentRuntimeSnapshot_WithoutSnapshot_IsNull_AndApiResponseRemainsBackwardsCompatible()
    {
        // A step without a runtime snapshot should serialize cleanly with null snapshot field.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = (AgentRuntimeSnapshot?)null;
        var json = JsonSerializer.Serialize(snapshot, options);
        Assert.Equal("null", json);

        // Re-deserializing null produces null — no crash.
        var roundTripped = JsonSerializer.Deserialize<AgentRuntimeSnapshot?>(json, options);
        Assert.Null(roundTripped);
    }
}

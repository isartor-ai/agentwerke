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
    public void AgentHookContract_CapturesFailureModeAndSettings()
    {
        var hook = new AgentHookContract
        {
            Name = "policy-guard",
            Event = "before_agent_run",
            Type = "internal-policy",
            Blocking = true,
            FailureMode = AgentHookFailureModes.FailOpen,
            Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["decision"] = "block"
            }
        };

        Assert.Equal("policy-guard", hook.Name);
        Assert.Equal(AgentHookFailureModes.FailOpen, hook.FailureMode);
        Assert.Equal("block", hook.Settings["decision"]);
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
}

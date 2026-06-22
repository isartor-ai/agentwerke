using Autofac.Agents.Models;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Tests;

public sealed class SandboxedAgentRuntimeExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_MapsAgentModelRunnerResultIntoSandboxedResult()
    {
        var executor = new SandboxedAgentRuntimeExecutor(
            new StubAgentModelRunner(new ModelRunResult(
                Succeeded: true,
                Output: "done",
                FailureReason: null,
                ToolInvocations:
                [
                    new AgentToolInvocationRecord
                    {
                        ToolName = "github.create_branch",
                        Category = AgentToolCategories.Integration,
                        Status = "completed"
                    }
                ],
                Artifacts: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evidence.md"] = "/artifacts/evidence.md"
                },
                TokenUsage: new AgentModelTokenUsage(10, 20, "claude-sonnet-4-6"))));

        var result = await executor.ExecuteAsync(MakeEnvelope(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("done", result.Output);
        Assert.Single(result.ToolInvocations!);
        Assert.Equal("github.create_branch", result.ToolInvocations[0].ToolName);
        Assert.Equal("/artifacts/evidence.md", result.Artifacts!["evidence.md"]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMcpIsConfigured_ReturnsClearFailure()
    {
        var executor = new SandboxedAgentRuntimeExecutor(new StubAgentModelRunner(
            new ModelRunResult(false, null, "should not run", [], null, null)));

        var result = await executor.ExecuteAsync(
            MakeEnvelope() with
            {
                Contract = new AgentRuntimeContract
                {
                    McpServers =
                    [
                        new AgentMcpServerContract { Name = "weather", Transport = "http", Url = "https://example.test/mcp" }
                    ]
                }
            },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("does not support MCP servers yet", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    private static SandboxedAgentRunEnvelope MakeEnvelope() =>
        new(
            RunId: "run-1",
            StepId: "step-1",
            AgentName: "github-agent",
            Action: "implement",
            Environment: "ci",
            PurposeType: "implementation",
            PolicyTag: "repo-change",
            Attempt: 1,
            SystemPrompt: "You are a GitHub agent.",
            UserPrompt: "Create the branch.",
            Model: "claude-sonnet-4-6",
            MaxTokens: 1024,
            Contract: new AgentRuntimeContract(),
            ResolvedTools:
            [
                new SandboxedToolContract("github.create_branch", AgentToolCategories.Integration)
            ],
            SubAgents: [],
            RemainingSubAgentDepth: 0);

    private sealed class StubAgentModelRunner : IAgentModelRunner
    {
        private readonly ModelRunResult _result;

        public StubAgentModelRunner(ModelRunResult result)
        {
            _result = result;
        }

        public Task<ModelRunResult> RunAsync(ModelRunRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }
}

using Autofac.Agents.Mcp;
using Autofac.Agents.Models;
using Autofac.Agents.Tools;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Tests;

public sealed class SandboxedAgentRuntimeExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenMcpToolIsUsed_ReturnsInvocation()
    {
        var executor = new SandboxedAgentRuntimeExecutor(
            new StubLanguageModelClient(request =>
                request.Tools.Any(static tool => string.Equals(tool.Name, "mcp.weather.lookup", StringComparison.OrdinalIgnoreCase))
                    ? new ToolCallingPlan("mcp.weather.lookup", new Dictionary<string, string> { ["location"] = "Berlin" }, "Forecast ready.")
                    : new ToolCallingPlan(null, null, "No tools.")),
            new StubMcpToolSessionFactory(new StubMcpToolSession([new StubMcpTool("mcp.weather.lookup")])));

        var result = await executor.ExecuteAsync(
            new SandboxedAgentRunEnvelope(
                RunId: "run-1",
                StepId: "step-1",
                AgentName: "weather-agent",
                Action: "weather.plan",
                Environment: "ci",
                PurposeType: "analysis",
                PolicyTag: "doc-generation",
                Attempt: 1,
                SystemPrompt: "You are a weather agent.",
                UserPrompt: "Check the forecast.",
                Model: "claude-sonnet-4-6",
                MaxTokens: 1024,
                Contract: new AgentRuntimeContract
                {
                    McpServers =
                    [
                        new AgentMcpServerContract { Name = "weather", Transport = "http", Url = "https://example.test/mcp" }
                    ]
                },
                SubAgents: [],
                RemainingSubAgentDepth: 0),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var invocation = Assert.Single(result.ToolInvocations!);
        Assert.Equal("mcp.weather.lookup", invocation.ToolName);
        Assert.Equal(AgentToolCategories.Mcp, invocation.Category);
        Assert.Equal("completed", invocation.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSubAgentDelegatesToMcpTool_ReturnsCorrelatedInvocations()
    {
        var executor = new SandboxedAgentRuntimeExecutor(
            new StubLanguageModelClient(request =>
            {
                if (request.Tools.Any(static tool => string.Equals(tool.Name, "subagent.weather-agent", StringComparison.OrdinalIgnoreCase)))
                {
                    return new ToolCallingPlan(
                        "subagent.weather-agent",
                        new Dictionary<string, string> { ["prompt"] = "Look up the Berlin weather." },
                        "Delegation complete.");
                }

                if (request.Tools.Any(static tool => string.Equals(tool.Name, "mcp.weather.lookup", StringComparison.OrdinalIgnoreCase)))
                {
                    return new ToolCallingPlan(
                        "mcp.weather.lookup",
                        new Dictionary<string, string> { ["location"] = "Berlin" },
                        "The weather is clear.");
                }

                return new ToolCallingPlan(null, null, "No tools.");
            }),
            new StubMcpToolSessionFactory(new StubMcpToolSession([new StubMcpTool("mcp.weather.lookup")])));

        var result = await executor.ExecuteAsync(
            new SandboxedAgentRunEnvelope(
                RunId: "run-1",
                StepId: "step-1",
                AgentName: "coordinator",
                Action: "weather.coordinate",
                Environment: "ci",
                PurposeType: "analysis",
                PolicyTag: "doc-generation",
                Attempt: 1,
                SystemPrompt: "You are a coordinator.",
                UserPrompt: "Delegate the weather lookup.",
                Model: "claude-sonnet-4-6",
                MaxTokens: 1024,
                Contract: new AgentRuntimeContract
                {
                    McpServers =
                    [
                        new AgentMcpServerContract { Name = "weather", Transport = "http", Url = "https://example.test/mcp" }
                    ],
                    SubAgents = new AgentSubAgentContract
                    {
                        Enabled = true,
                        MaxDepth = 1,
                        AllowedAgents = ["weather-agent"]
                    }
                },
                SubAgents:
                [
                    new SandboxedSubAgentProfile(
                        "weather-agent",
                        "Weather Agent",
                        "Looks up forecasts.",
                        "You are a weather specialist.",
                        null)
                ],
                RemainingSubAgentDepth: 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.ToolInvocations!.Count);
        Assert.Equal("subagent.weather-agent", result.ToolInvocations[0].ToolName);
        Assert.Equal(AgentToolCategories.SubAgent, result.ToolInvocations[0].Category);
        Assert.Equal("completed", result.ToolInvocations[0].Status);
        Assert.Equal("mcp.weather.lookup", result.ToolInvocations[1].ToolName);
        Assert.Equal(AgentToolCategories.Mcp, result.ToolInvocations[1].Category);
        Assert.Equal("completed", result.ToolInvocations[1].Status);
    }

    private sealed class StubLanguageModelClient : ILanguageModelClient
    {
        private readonly Func<LanguageModelRequest, ToolCallingPlan> _planBuilder;

        public StubLanguageModelClient(Func<LanguageModelRequest, ToolCallingPlan> planBuilder)
        {
            _planBuilder = planBuilder;
        }

        public async Task<LanguageModelResponse> RunAsync(
            LanguageModelRequest request,
            Func<LanguageModelToolCall, CancellationToken, Task<LanguageModelToolResult>> toolExecutor,
            CancellationToken cancellationToken)
        {
            var plan = _planBuilder(request);
            if (!string.IsNullOrWhiteSpace(plan.ToolName))
            {
                await toolExecutor(
                    new LanguageModelToolCall("call-1", plan.ToolName!, plan.Input!),
                    cancellationToken);
            }

            return new LanguageModelResponse(
                Succeeded: true,
                Output: plan.FinalOutput,
                FailureReason: null,
                AllToolCalls: [],
                Usage: new LanguageModelTokenUsage(10, 20),
                ModelId: "claude-sonnet-4-6");
        }
    }

    private sealed record ToolCallingPlan(
        string? ToolName,
        IReadOnlyDictionary<string, string>? Input,
        string FinalOutput);

    private sealed class StubMcpToolSessionFactory : IMcpToolSessionFactory
    {
        private readonly IMcpToolSession _session;

        public StubMcpToolSessionFactory(IMcpToolSession session)
        {
            _session = session;
        }

        public Task<McpToolSessionResult> CreateAsync(
            IReadOnlyList<AgentMcpServerContract> servers,
            CancellationToken cancellationToken) =>
            Task.FromResult(new McpToolSessionResult(true, _session, null));
    }

    private sealed class StubMcpToolSession : IMcpToolSession
    {
        public StubMcpToolSession(IReadOnlyList<IAgentTool> tools)
        {
            Tools = tools;
        }

        public IReadOnlyList<IAgentTool> Tools { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubMcpTool : IAgentTool, IToolSchemaProvider
    {
        public StubMcpTool(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Category => AgentToolCategories.Mcp;

        public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
        [
            new ToolSchemaParameter("location", "string", "Location to look up.", Required: true)
        ];

        public void Validate(IReadOnlyDictionary<string, string> input)
        {
            if (!input.ContainsKey("location"))
            {
                throw new InvalidOperationException("location is required");
            }
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(
            AgentToolExecutionContext context,
            IReadOnlyDictionary<string, string> input,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AgentToolExecutionResult(
                Succeeded: true,
                Output: $"Forecast for {input["location"]}",
                FailureReason: null));
    }
}

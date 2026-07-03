using Autofac.Agents.Mcp;
using Autofac.Agents.Models;
using Autofac.Agents.Tools;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Tests;

public sealed class SandboxedAgentRuntimeExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenIntegrationToolIsUsed_InjectsRunContextAndReturnsInvocation()
    {
        var tool = new StubDirectTool("github.create_pull_request", AgentToolCategories.Integration);
        var executor = new SandboxedAgentRuntimeExecutor(
            new StubLanguageModelClient(request =>
                request.Tools.Any(static t => string.Equals(t.Name, "github.create_pull_request", StringComparison.OrdinalIgnoreCase))
                    ? new ToolCallingPlan(
                        "github.create_pull_request",
                        new Dictionary<string, string>
                        {
                            ["head_branch"] = "agentwerke/run-1",
                            ["title"] = "Autofac PR",
                            ["body"] = "Body",
                            ["commit_message"] = "Commit"
                        },
                        "PR ready.")
                    : new ToolCallingPlan(null, null, "No tools.")),
            new StubMcpToolSessionFactory(new StubMcpToolSession([])),
            new ToolRegistry([tool]));

        var result = await executor.ExecuteAsync(
            MakeEnvelope(
                resolvedTools:
                [
                    new SandboxedToolContract("github.create_pull_request", AgentToolCategories.Integration)
                ]),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var invocation = Assert.Single(result.ToolInvocations!);
        Assert.Equal("github.create_pull_request", invocation.ToolName);
        Assert.Equal(AgentToolCategories.Integration, invocation.Category);
        Assert.Equal("completed", invocation.Status);
        Assert.NotNull(tool.LastInput);
        Assert.Equal("run-1", tool.LastInput!["run_id"]);
        Assert.Equal("step-1", tool.LastInput["step_id"]);
        Assert.Equal("1", tool.LastInput["attempt"]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMcpToolIsUsed_ReturnsInvocation()
    {
        var executor = new SandboxedAgentRuntimeExecutor(
            new StubLanguageModelClient(request =>
                request.Tools.Any(static tool => string.Equals(tool.Name, "mcp.weather.lookup", StringComparison.OrdinalIgnoreCase))
                    ? new ToolCallingPlan("mcp.weather.lookup", new Dictionary<string, string> { ["location"] = "Berlin" }, "Forecast ready.")
                    : new ToolCallingPlan(null, null, "No tools.")),
            new StubMcpToolSessionFactory(new StubMcpToolSession([new StubMcpTool("mcp.weather.lookup")])),
            new ToolRegistry([]));

        var result = await executor.ExecuteAsync(
            MakeEnvelope(contract: new AgentRuntimeContract
            {
                McpServers =
                [
                    new AgentMcpServerContract { Name = "weather", Transport = "http", Url = "https://example.test/mcp" }
                ]
            }),
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
            new StubMcpToolSessionFactory(new StubMcpToolSession([new StubMcpTool("mcp.weather.lookup")])),
            new ToolRegistry([]));

        var result = await executor.ExecuteAsync(
            MakeEnvelope(
                agentName: "coordinator",
                action: "weather.coordinate",
                purposeType: "analysis",
                contract: new AgentRuntimeContract
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
                subAgents:
                [
                    new SandboxedSubAgentProfile(
                        "weather-agent",
                        "Weather Agent",
                        "Looks up forecasts.",
                        "You are a weather specialist.",
                        null)
                ],
                remainingSubAgentDepth: 1),
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

    private static SandboxedAgentRunEnvelope MakeEnvelope(
        string agentName = "weather-agent",
        string action = "weather.plan",
        string purposeType = "analysis",
        AgentRuntimeContract? contract = null,
        IReadOnlyList<SandboxedToolContract>? resolvedTools = null,
        IReadOnlyList<SandboxedSubAgentProfile>? subAgents = null,
        int remainingSubAgentDepth = 0) =>
        new(
            RunId: "run-1",
            StepId: "step-1",
            AgentName: agentName,
            Action: action,
            Environment: "ci",
            PurposeType: purposeType,
            PolicyTag: "doc-generation",
            Attempt: 1,
            SystemPrompt: "You are a weather agent.",
            UserPrompt: "Check the forecast.",
            Model: "claude-sonnet-4-6",
            MaxTokens: 1024,
            Contract: contract ?? new AgentRuntimeContract(),
            ResolvedTools: resolvedTools ?? [],
            SubAgents: subAgents ?? [],
            RemainingSubAgentDepth: remainingSubAgentDepth);

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

    private sealed class StubDirectTool : IAgentTool, IToolSchemaProvider
    {
        public StubDirectTool(string name, string category)
        {
            Name = name;
            Category = category;
        }

        public string Name { get; }

        public string Category { get; }

        public IReadOnlyDictionary<string, string>? LastInput { get; private set; }

        public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
        [
            new("head_branch", "string", "The source branch to merge from.", Required: true),
            new("title", "string", "The pull request title.", Required: true),
            new("body", "string", "The pull request description body.", Required: true),
            new("commit_message", "string", "Commit message for evidence commits.", Required: true),
            new("run_id", "string", "The workflow run ID (injected automatically).", Required: false),
            new("step_id", "string", "The workflow step ID (injected automatically).", Required: false),
            new("attempt", "string", "The execution attempt number (injected automatically).", Required: false)
        ];

        public void Validate(IReadOnlyDictionary<string, string> input)
        {
            foreach (var key in new[] { "head_branch", "title", "body", "commit_message", "run_id", "step_id", "attempt" })
            {
                if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"{key} is required");
                }
            }
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(
            AgentToolExecutionContext context,
            IReadOnlyDictionary<string, string> input,
            CancellationToken cancellationToken)
        {
            LastInput = new Dictionary<string, string>(input, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new AgentToolExecutionResult(
                Succeeded: true,
                Output: "Created pull request.",
                FailureReason: null));
        }
    }
}

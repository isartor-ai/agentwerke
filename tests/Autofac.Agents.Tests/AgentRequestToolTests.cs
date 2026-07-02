using Autofac.Agents;
using Autofac.Agents.Models;
using Autofac.Agents.Tools;
using Autofac.Application.Agents;
using Autofac.Domain.AgentRuntime;
using Autofac.Domain.Persistence;

namespace Autofac.Agents.Tests;

public sealed class AgentRequestToolTests
{
    private static AgentToolExecutionContext Context(string agent = "planner") =>
        new("run-1", "step-1", agent, "agent.request", null, "general", "tag", 1);

    private static AgentRequestTool BuildTool(
        out FakeAgentModelRunner runner,
        out FakeInteractionStore store,
        params AgentProfile[] profiles)
    {
        runner = new FakeAgentModelRunner(new ModelRunResult(
            Succeeded: true, Output: "scaffolding done", FailureReason: null,
            ToolInvocations: [], Artifacts: null, TokenUsage: null));
        store = new FakeInteractionStore();
        return new AgentRequestTool(new FakeRegistry(profiles), new StubServiceProvider(runner), store);
    }

    [Fact]
    public async Task Delegates_RunsCalleeInline_ReturnsResult_AndRecordsRequestAndReply()
    {
        var tool = BuildTool(out var runner, out var store,
            new AgentProfile { AgentId = "coder", Name = "coder", SystemPrompt = "You are a coder." });

        var result = await tool.ExecuteAsync(
            Context("planner"),
            new Dictionary<string, string> { ["to"] = "coder", ["task"] = "scaffold the orders API" },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("scaffolding done", result.Output);

        // Two interactions, request then reply, linked by one correlation id.
        Assert.Equal(2, store.Items.Count);
        Assert.All(store.Items, i => Assert.Equal(AgentInteractionKinds.AgentRequest, i.Kind));
        Assert.Equal(store.Items[0].CorrelationId, store.Items[1].CorrelationId);
        Assert.Equal("planner", store.Items[0].FromAgent);
        Assert.Equal("coder", store.Items[0].Addressee);
        Assert.Equal("coder", store.Items[1].FromAgent);
        Assert.Equal("planner", store.Items[1].Addressee);

        // The callee ran read-only and could neither delegate again nor ask a human (depth guard).
        Assert.NotNull(runner.LastRequest);
        Assert.Equal("coder", runner.LastRequest!.AgentName);
        Assert.Equal(AgentPermissionLevels.ReadOnly, runner.LastRequest.Contract.Permissions.Level);
        Assert.Contains("agent.request", runner.LastRequest.Contract.Permissions.DeniedTools);
        Assert.Contains("human.ask", runner.LastRequest.Contract.Permissions.DeniedTools);
        Assert.Contains("scaffold the orders API", runner.LastRequest.PromptSnapshot.FinalPrompt);
        Assert.Contains("You are a coder.", runner.LastRequest.PromptSnapshot.FinalPrompt);
    }

    [Fact]
    public async Task Delegating_ToSelf_IsRejected_WithoutRunningOrRecording()
    {
        var tool = BuildTool(out var runner, out var store,
            new AgentProfile { AgentId = "planner", Name = "planner" });

        var result = await tool.ExecuteAsync(
            Context("planner"),
            new Dictionary<string, string> { ["to"] = "planner", ["task"] = "do it yourself" },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(runner.LastRequest);
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task Delegating_ToUnknownAgent_Fails_WithoutRunning()
    {
        var tool = BuildTool(out var runner, out var store);

        var result = await tool.ExecuteAsync(
            Context("planner"),
            new Dictionary<string, string> { ["to"] = "ghost", ["task"] = "anything" },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Unknown agent", result.FailureReason);
        Assert.Null(runner.LastRequest);
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task WhenCalleeFails_ReportsFailure_AndRecordsFailureReply()
    {
        var store = new FakeInteractionStore();
        var runner = new FakeAgentModelRunner(new ModelRunResult(
            Succeeded: false, Output: null, FailureReason: "model not configured",
            ToolInvocations: [], Artifacts: null, TokenUsage: null));
        var tool = new AgentRequestTool(
            new FakeRegistry(new AgentProfile { AgentId = "coder", Name = "coder" }),
            new StubServiceProvider(runner), store);

        var result = await tool.ExecuteAsync(
            Context("planner"),
            new Dictionary<string, string> { ["to"] = "coder", ["task"] = "scaffold" },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("model not configured", result.FailureReason);
        Assert.Equal(2, store.Items.Count);
        Assert.Contains("model not configured", store.Items[1].Prompt);
    }

    [Fact]
    public void Validate_MissingFields_Throws()
    {
        var tool = BuildTool(out _, out _);
        Assert.Throws<InvalidOperationException>(() =>
            tool.Validate(new Dictionary<string, string> { ["to"] = "coder" }));
        Assert.Throws<InvalidOperationException>(() =>
            tool.Validate(new Dictionary<string, string> { ["task"] = "x" }));
    }

    private sealed class FakeRegistry : IAgentRegistry
    {
        private readonly Dictionary<string, AgentProfile> _byId;

        public FakeRegistry(params AgentProfile[] profiles) =>
            _byId = profiles.ToDictionary(p => p.AgentId, StringComparer.OrdinalIgnoreCase);

        public AgentProfile? Find(string agentId) => _byId.GetValueOrDefault(agentId);

        public IReadOnlyList<AgentProfile> All() => _byId.Values.ToList();
    }

    private sealed class StubServiceProvider : IServiceProvider
    {
        private readonly IAgentModelRunner _runner;

        public StubServiceProvider(IAgentModelRunner runner) => _runner = runner;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IAgentModelRunner) ? _runner : null;
    }

    private sealed class FakeAgentModelRunner : IAgentModelRunner
    {
        private readonly ModelRunResult _result;

        public FakeAgentModelRunner(ModelRunResult result) => _result = result;

        public ModelRunRequest? LastRequest { get; private set; }

        public Task<ModelRunResult> RunAsync(ModelRunRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeInteractionStore : IAgentInteractionRepository
    {
        public List<AgentInteraction> Items { get; } = new();

        public Task AddAsync(AgentInteraction interaction, CancellationToken cancellationToken)
        {
            Items.Add(interaction);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentInteraction>> GetByRunAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentInteraction>>(Items.Where(i => i.RunId == runId).ToList());

        public Task<IReadOnlyList<AgentInteraction>> GetPostsForRunAsync(
            string runId, string? fromFilter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentInteraction>>(
                Items.Where(i => i.RunId == runId && i.Kind == AgentInteractionKinds.Post).ToList());

        public Task<AgentInteraction?> GetByIdAsync(string interactionId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(i => i.Id == interactionId));

        public Task<AgentInteraction?> GetPendingForRunAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(i =>
                i.RunId == runId && i.Status == AgentInteractionStatuses.Pending));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

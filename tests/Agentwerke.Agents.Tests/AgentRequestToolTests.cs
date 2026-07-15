using Agentwerke.Agents;
using Agentwerke.Agents.Models;
using Agentwerke.Agents.Tools;
using Agentwerke.Application.Agents;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;
using Agentwerke.Workflows.Runtime;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents.Tests;

public sealed class AgentRequestToolTests
{
    private static AgentToolExecutionContext Context(string agent = "planner") =>
        new("run-1", "step-1", agent, "agent.request", null, "general", "tag", 1);

    private static AgentRequestTool BuildTool(
        out FakeAgentModelRunner runner,
        out InMemoryInteractionRepository store,
        params AgentProfile[] profiles)
    {
        runner = new FakeAgentModelRunner(new ModelRunResult(
            Succeeded: true, Output: "scaffolding done", FailureReason: null,
            ToolInvocations: [], Artifacts: null, TokenUsage: null));
        store = new InMemoryInteractionRepository();
        var capturedRunner = runner;
        return new AgentRequestTool(
            new FakeRegistry(profiles), new Lazy<IAgentModelRunner>(() => capturedRunner), store,
            Options.Create(new AgentRequestOptions()));
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
        Assert.DoesNotContain("agent.request", runner.LastRequest.Contract.Permissions.DeniedTools);
        Assert.Contains("human.ask", runner.LastRequest.Contract.Permissions.DeniedTools);
        Assert.Contains("human.confirm", runner.LastRequest.Contract.Permissions.DeniedTools);
        Assert.Equal(1, runner.LastRequest.DelegationDepth);
        Assert.Equal(["planner"], runner.LastRequest.DelegationChain);
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
        var store = new InMemoryInteractionRepository();
        var runner = new FakeAgentModelRunner(new ModelRunResult(
            Succeeded: false, Output: null, FailureReason: "model not configured",
            ToolInvocations: [], Artifacts: null, TokenUsage: null));
        var tool = new AgentRequestTool(
            new FakeRegistry(new AgentProfile { AgentId = "coder", Name = "coder" }),
            new Lazy<IAgentModelRunner>(() => runner), store, Options.Create(new AgentRequestOptions()));

        var result = await tool.ExecuteAsync(
            Context("planner"),
            new Dictionary<string, string> { ["to"] = "coder", ["task"] = "scaffold" },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("model not configured", result.FailureReason);
        Assert.Equal(2, store.Items.Count);
        Assert.Contains("model not configured", store.Items[1].Prompt);
        Assert.Equal(AgentInteractionStatuses.Answered, store.Items[0].Status);
        Assert.Equal(AgentInteractionStatuses.Answered, store.Items[1].Status);
        Assert.Equal("coder", store.Items[1].RespondedBy);
        Assert.Equal(InteractionChannels.Agent, store.Items[1].RespondedChannel);
    }

    [Fact]
    public async Task NonBlocking_PersistsPostedRequest_WithoutRunningCallee()
    {
        var tool = BuildTool(out var runner, out var store,
            new AgentProfile { AgentId = "coder", Name = "coder" });

        var result = await tool.ExecuteAsync(Context(), new Dictionary<string, string>
        {
            ["to"] = "coder", ["task"] = "investigate", ["blocking"] = "false"
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Dispatched to coder.", result.Output);
        Assert.Null(runner.LastRequest);
        var request = Assert.Single(store.Items);
        Assert.False(request.Blocking);
        Assert.Equal(AgentInteractionStatuses.Posted, request.Status);
    }

    [Fact]
    public async Task DepthLimitAndCycle_AreRejectedWithoutInvocation()
    {
        var tool = BuildTool(out var runner, out var store,
            new AgentProfile { AgentId = "coder", Name = "coder" });
        var atLimit = Context() with { DelegationDepth = 3, DelegationChain = ["root"] };
        var depth = await tool.ExecuteAsync(atLimit,
            new Dictionary<string, string> { ["to"] = "coder", ["task"] = "x" }, CancellationToken.None);
        Assert.False(depth.Succeeded);
        Assert.Contains("maximum delegation depth", depth.FailureReason, StringComparison.OrdinalIgnoreCase);

        var cycleContext = Context() with { DelegationDepth = 1, DelegationChain = ["coder"] };
        var cycle = await tool.ExecuteAsync(cycleContext,
            new Dictionary<string, string> { ["to"] = "coder", ["task"] = "x" }, CancellationToken.None);
        Assert.False(cycle.Succeeded);
        Assert.Contains("cycle", cycle.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(runner.LastRequest);
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task ThrownCalleeFailure_IsSurfacedButCancellationPropagates()
    {
        var profile = new AgentProfile { AgentId = "coder", Name = "coder" };
        var store = new InMemoryInteractionRepository();
        var throwing = new ThrowingRunner(new InvalidOperationException("provider exploded"));
        var tool = new AgentRequestTool(new FakeRegistry(profile), new Lazy<IAgentModelRunner>(() => throwing), store,
            Options.Create(new AgentRequestOptions()));
        var result = await tool.ExecuteAsync(Context(),
            new Dictionary<string, string> { ["to"] = "coder", ["task"] = "x" }, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Contains("provider exploded", result.FailureReason);

        var cancelled = new ThrowingRunner(new OperationCanceledException());
        tool = new AgentRequestTool(new FakeRegistry(profile), new Lazy<IAgentModelRunner>(() => cancelled),
            new InMemoryInteractionRepository(), Options.Create(new AgentRequestOptions()));
        await Assert.ThrowsAsync<OperationCanceledException>(() => tool.ExecuteAsync(Context(),
            new Dictionary<string, string> { ["to"] = "coder", ["task"] = "x" }, CancellationToken.None));
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

    private sealed class FakeAgentModelRunner : IAgentModelRunner
    {
        private readonly ModelRunResult _result;

        public FakeAgentModelRunner(ModelRunResult result) => _result = result;

        public ModelRunRequest? LastRequest { get; private set; }

        public Task<ModelRunResult> RunAsync(
            ModelRunRequest request,
            CancellationToken cancellationToken,
            AgentExecutionProgressReporter? progressReporter = null)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingRunner(Exception exception) : IAgentModelRunner
    {
        public Task<ModelRunResult> RunAsync(ModelRunRequest request, CancellationToken cancellationToken,
            AgentExecutionProgressReporter? progressReporter = null) => Task.FromException<ModelRunResult>(exception);
    }

}

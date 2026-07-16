using Agentwerke.Agents.Tools;
using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;
using Agentwerke.Application.Workflows;

namespace Agentwerke.Agents.Tests;

public sealed class HumanInteractionToolsTests
{
    private static AgentToolExecutionContext Context(string runId, string agent) =>
        new(runId, "step-1", agent, "human.ask", null, "general", "tag", 1, NodeId: "node-1");

    private static HumanAskTool Ask(InMemoryInteractionRepository store, InMemoryRunContextRepository context, RecordingRouter? router = null) =>
        new(store, context, router ?? new RecordingRouter(), new InteractionOptions());

    [Fact]
    public async Task Ask_FirstCall_PersistsPendingInteractionAndSuspends()
    {
        var store = new InMemoryInteractionRepository();
        var runContext = new InMemoryRunContextRepository();
        var router = new RecordingRouter();
        var ask = Ask(store, runContext, router);

        var ex = await Assert.ThrowsAsync<AgentInteractionRequiredException>(() =>
            ask.ExecuteAsync(
                Context("run-1", "reviewer"),
                new Dictionary<string, string> { ["question"] = "Ship or add tests?", ["options"] = "ship, add tests" },
                CancellationToken.None));

        var pending = Assert.Single(store.Items);
        Assert.Equal(ex.InteractionId, pending.Id);
        Assert.Equal(AgentInteractionKinds.Choice, pending.Kind);
        Assert.Equal(AgentInteractionAddresseeTypes.Human, pending.AddresseeType);
        Assert.True(pending.Blocking);
        Assert.Equal(AgentInteractionStatuses.Pending, pending.Status);
        Assert.Equal(new[] { "ship", "add tests" }, pending.Options);
        Assert.Equal("step-1", pending.StepId);
        Assert.Equal(pending.Id, (await runContext.GetAllAsync("run-1", CancellationToken.None)).Single().Value);
        Assert.Same(pending, Assert.Single(router.Routed));
    }

    [Fact]
    public async Task Ask_AfterAnswer_ReRunReturnsAnswerWithoutAskingAgain()
    {
        var store = new InMemoryInteractionRepository();
        var runContext = new InMemoryRunContextRepository();
        var ask = Ask(store, runContext);
        var input = new Dictionary<string, string> { ["question"] = "Which auth scheme?" };

        // First pass suspends.
        await Assert.ThrowsAsync<AgentInteractionRequiredException>(() =>
            ask.ExecuteAsync(Context("run-1", "coder"), input, CancellationToken.None));

        // Human answers.
        var interaction = Assert.Single(store.Items);
        interaction.Status = AgentInteractionStatuses.Answered;
        interaction.Response = "SessionAuth";
        interaction.RespondedChannel = InteractionChannels.Slack;

        // Re-run: same question returns the answer, no new interaction, no throw.
        var result = await ask.ExecuteAsync(Context("run-1", "coder"), input, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("SessionAuth", result.Output);
        Assert.Contains("UNTRUSTED HUMAN RESPONSE", result.Output);
        Assert.Single(store.Items);
        Assert.Empty(await runContext.GetAllAsync("run-1", CancellationToken.None));
    }

    [Fact]
    public async Task Ask_WhileStillPending_ReThrowsToKeepWaiting()
    {
        var store = new InMemoryInteractionRepository();
        var ask = Ask(store, new InMemoryRunContextRepository());
        var input = new Dictionary<string, string> { ["question"] = "Proceed?" };

        await Assert.ThrowsAsync<AgentInteractionRequiredException>(() =>
            ask.ExecuteAsync(Context("run-1", "planner"), input, CancellationToken.None));

        // Same question again while still pending must not create a duplicate.
        await Assert.ThrowsAsync<AgentInteractionRequiredException>(() =>
            ask.ExecuteAsync(Context("run-1", "planner"), input, CancellationToken.None));

        Assert.Single(store.Items);
    }

    [Fact]
    public void Ask_MissingQuestion_Throws()
    {
        var ask = Ask(new InMemoryInteractionRepository(), new InMemoryRunContextRepository());
        Assert.Throws<InvalidOperationException>(() => ask.Validate(new Dictionary<string, string>()));
    }

    [Fact]
    public async Task Notify_PersistsNonBlockingInteraction_AndDoesNotSuspend()
    {
        var store = new InMemoryInteractionRepository();
        var router = new RecordingRouter();
        var notify = new HumanNotifyTool(store, router, new InteractionOptions());

        var result = await notify.ExecuteAsync(
            Context("run-1", "reviewer"),
            new Dictionary<string, string> { ["message"] = "Heads up: coverage dropped." },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var item = Assert.Single(store.Items);
        Assert.Equal(AgentInteractionKinds.Notify, item.Kind);
        Assert.False(item.Blocking);
        Assert.Equal(AgentInteractionStatuses.Posted, item.Status);
        Assert.Same(item, Assert.Single(router.Routed));
    }

    [Fact]
    public async Task Ask_RephrasedQuestionOnReRun_UsesNodeKeyAndReturnsOriginalAnswer()
    {
        var store = new InMemoryInteractionRepository();
        var runContext = new InMemoryRunContextRepository();
        var ask = Ask(store, runContext);
        await Assert.ThrowsAsync<AgentInteractionRequiredException>(() => ask.ExecuteAsync(
            Context("run-1", "planner"), new Dictionary<string, string> { ["question"] = "Use OAuth?" }, CancellationToken.None));
        store.Items[0].Status = AgentInteractionStatuses.Answered;
        store.Items[0].Response = "yes";
        store.Items[0].RespondedChannel = InteractionChannels.Teams;

        var result = await ask.ExecuteAsync(Context("run-1", "planner"),
            new Dictionary<string, string> { ["question"] = "Should OAuth be used?" }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("yes", result.Output);
        Assert.Single(store.Items);
    }

    [Fact]
    public async Task Confirm_Rejected_ThrowsConfirmationRejected()
    {
        var store = new InMemoryInteractionRepository();
        var runContext = new InMemoryRunContextRepository();
        var confirm = new HumanConfirmTool(store, runContext, new RecordingRouter(), new InteractionOptions());
        var input = new Dictionary<string, string> { ["question"] = "Deploy now?" };
        await Assert.ThrowsAsync<AgentInteractionRequiredException>(() =>
            confirm.ExecuteAsync(Context("run-1", "deployer"), input, CancellationToken.None));
        store.Items[0].Status = AgentInteractionStatuses.Rejected;
        store.Items[0].Response = "reject";

        await Assert.ThrowsAsync<ConfirmationRejectedException>(() =>
            confirm.ExecuteAsync(Context("run-1", "deployer"), input, CancellationToken.None));
    }

    private sealed class RecordingRouter : IInteractionRouter
    {
        public List<AgentInteraction> Routed { get; } = [];
        public Task RouteAsync(AgentInteraction interaction, CancellationToken cancellationToken)
        {
            Routed.Add(interaction);
            return Task.CompletedTask;
        }
        public Task<InteractionDeliveryResult> RetryAsync(string interactionId, string channel, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

}

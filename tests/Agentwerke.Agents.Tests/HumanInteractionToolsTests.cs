using Agentwerke.Agents.Tools;
using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;

namespace Agentwerke.Agents.Tests;

public sealed class HumanInteractionToolsTests
{
    private static AgentToolExecutionContext Context(string runId, string agent) =>
        new(runId, "step-1", agent, "human.ask", null, "general", "tag", 1);

    [Fact]
    public async Task Ask_FirstCall_PersistsPendingInteractionAndSuspends()
    {
        var store = new FakeInteractionStore();
        var ask = new HumanAskTool(store);

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
    }

    [Fact]
    public async Task Ask_AfterAnswer_ReRunReturnsAnswerWithoutAskingAgain()
    {
        var store = new FakeInteractionStore();
        var ask = new HumanAskTool(store);
        var input = new Dictionary<string, string> { ["question"] = "Which auth scheme?" };

        // First pass suspends.
        await Assert.ThrowsAsync<AgentInteractionRequiredException>(() =>
            ask.ExecuteAsync(Context("run-1", "coder"), input, CancellationToken.None));

        // Human answers.
        var interaction = Assert.Single(store.Items);
        interaction.Status = AgentInteractionStatuses.Answered;
        interaction.Response = "SessionAuth";

        // Re-run: same question returns the answer, no new interaction, no throw.
        var result = await ask.ExecuteAsync(Context("run-1", "coder"), input, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("SessionAuth", result.Output);
        Assert.Single(store.Items);
    }

    [Fact]
    public async Task Ask_WhileStillPending_ReThrowsToKeepWaiting()
    {
        var store = new FakeInteractionStore();
        var ask = new HumanAskTool(store);
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
        var ask = new HumanAskTool(new FakeInteractionStore());
        Assert.Throws<InvalidOperationException>(() => ask.Validate(new Dictionary<string, string>()));
    }

    [Fact]
    public async Task Notify_PersistsNonBlockingInteraction_AndDoesNotSuspend()
    {
        var store = new FakeInteractionStore();
        var notify = new HumanNotifyTool(store);

        var result = await notify.ExecuteAsync(
            Context("run-1", "reviewer"),
            new Dictionary<string, string> { ["message"] = "Heads up: coverage dropped." },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var item = Assert.Single(store.Items);
        Assert.Equal(AgentInteractionKinds.Notify, item.Kind);
        Assert.False(item.Blocking);
        Assert.Equal(AgentInteractionStatuses.Posted, item.Status);
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
            Task.FromResult<IReadOnlyList<AgentInteraction>>(
                Items.Where(i => i.RunId == runId).OrderBy(i => i.CreatedAt).ToList());

        public Task<IReadOnlyList<AgentInteraction>> GetPostsForRunAsync(
            string runId, string? fromFilter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentInteraction>>(
                Items.Where(i => i.RunId == runId && i.Kind == AgentInteractionKinds.Post).ToList());

        public Task<AgentInteraction?> GetByIdAsync(string interactionId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(i => i.Id == interactionId));

        public Task<AgentInteraction?> GetPendingForRunAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult(Items
                .Where(i => i.RunId == runId && i.Status == AgentInteractionStatuses.Pending)
                .OrderBy(i => i.CreatedAt)
                .FirstOrDefault());

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

using Autofac.Agents.Coordination;
using Autofac.Agents.Tools;
using Autofac.Application.Agents;
using Autofac.Domain.Persistence;

namespace Autofac.Agents.Tests;

public sealed class AgentCoordinationTests
{
    private static AgentToolExecutionContext Context(string runId, string agent) =>
        new(runId, "step", agent, "agent.post_message", null, "general", "tag", 1);

    [Fact]
    public async Task Channel_AppendsAndReadsInOrder_PerRun()
    {
        var channel = new InMemoryAgentCoordinationChannel();
        await channel.PostAsync("run-1", "ba", "requirements ready");
        await channel.PostAsync("run-1", "architect", "design ready");
        await channel.PostAsync("run-2", "other", "different run");

        var run1 = await channel.ReadAsync("run-1");
        Assert.Equal(2, run1.Count);
        Assert.Equal("ba", run1[0].From);
        Assert.Equal("architect", run1[1].From);
        Assert.Single(await channel.ReadAsync("run-2"));
    }

    [Fact]
    public async Task Channel_FiltersBySender()
    {
        var channel = new InMemoryAgentCoordinationChannel();
        await channel.PostAsync("run-1", "ba", "a");
        await channel.PostAsync("run-1", "architect", "b");

        var fromBa = await channel.ReadAsync("run-1", "ba");
        Assert.Single(fromBa);
        Assert.Equal("ba", fromBa[0].From);
    }

    [Fact]
    public async Task PostAndReadTools_RoundTrip()
    {
        var channel = new InMemoryAgentCoordinationChannel();
        var post = new AgentPostMessageTool(channel);
        var read = new AgentReadMessagesTool(channel);

        await post.ExecuteAsync(
            Context("run-1", "ba"),
            new Dictionary<string, string> { ["text"] = "requirements ready" },
            CancellationToken.None);

        var result = await read.ExecuteAsync(
            Context("run-1", "architect"),
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("ba: requirements ready", result.Output);
    }

    [Fact]
    public void PostTool_MissingText_Throws()
    {
        var tool = new AgentPostMessageTool(new InMemoryAgentCoordinationChannel());
        Assert.Throws<InvalidOperationException>(() => tool.Validate(new Dictionary<string, string>()));
    }

    [Fact]
    public async Task ReadTool_NoMessages_ReturnsFriendlyMessage()
    {
        var read = new AgentReadMessagesTool(new InMemoryAgentCoordinationChannel());
        var result = await read.ExecuteAsync(
            Context("run-empty", "architect"),
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("No coordination messages", result.Output!);
    }

    [Fact]
    public async Task PersistentChannel_SurvivesSimulatedRestart()
    {
        // Shared store stands in for the database; a fresh channel over the same store
        // is the "after restart" reader (#192 Phase 1).
        var store = new FakeInteractionStore();

        var writer = new PersistentAgentCoordinationChannel(store);
        await writer.PostAsync("run-1", "ba", "requirements ready", stepId: "step-2");
        await writer.PostAsync("run-1", "architect", "design ready");

        var afterRestart = new PersistentAgentCoordinationChannel(store);
        var messages = await afterRestart.ReadAsync("run-1");

        Assert.Equal(2, messages.Count);
        Assert.Equal("ba", messages[0].From);
        Assert.Equal("architect", messages[1].From);

        var fromBa = await afterRestart.ReadAsync("run-1", "ba");
        Assert.Single(fromBa);

        // Stored as persisted post interactions, anchored to the producing step.
        Assert.All(store.Items, i => Assert.Equal(AgentInteractionKinds.Post, i.Kind));
        Assert.Equal("step-2", store.Items[0].StepId);
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
            string runId, string? fromFilter, CancellationToken cancellationToken)
        {
            var query = Items.Where(i => i.RunId == runId && i.Kind == AgentInteractionKinds.Post);
            if (!string.IsNullOrWhiteSpace(fromFilter))
            {
                query = query.Where(i => i.FromAgent == fromFilter);
            }

            return Task.FromResult<IReadOnlyList<AgentInteraction>>(query.OrderBy(i => i.CreatedAt).ToList());
        }

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

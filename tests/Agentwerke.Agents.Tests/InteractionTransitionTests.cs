using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;

namespace Agentwerke.Agents.Tests;

/// <summary>
/// Contract tests for the single-winner transition primitive (#218). These pin the semantics every
/// caller depends on: the orchestration verbs (#219), the sweeper (#221), and the inbound channel
/// adapters (#224, #225) all decide whether to resume a run purely from the outcome returned here.
///
/// Scope note: these exercise the in-memory fake, which reproduces the database's atomicity with a
/// lock. That proves the *contract*. Proof that Postgres actually enforces the Version token — i.e.
/// that EF raises DbUpdateConcurrencyException — needs a real database and belongs to the E2E work
/// (#230); it cannot be asserted here because this repo has no in-process EF provider.
/// </summary>
public sealed class InteractionTransitionTests
{
    [Fact]
    public async Task TryTransition_MovesPendingInteractionToTerminalStatus()
    {
        var repository = new InMemoryInteractionRepository();
        var interaction = Pending("i1");
        await repository.AddAsync(interaction, CancellationToken.None);

        var result = await repository.TryTransitionAsync(
            "i1", AgentInteractionStatuses.Answered, "approve", "dana", InteractionChannels.Ui,
            CancellationToken.None);

        Assert.Equal(InteractionTransitionOutcome.Won, result.Outcome);
        Assert.Equal(AgentInteractionStatuses.Answered, interaction.Status);
        Assert.Equal("approve", interaction.Response);
        Assert.Equal("dana", interaction.RespondedBy);
        Assert.Equal(InteractionChannels.Ui, interaction.RespondedChannel);
        Assert.NotNull(interaction.RespondedAt);
        Assert.Equal(1, interaction.Version);
    }

    [Fact]
    public async Task TryTransition_ReturnsNotFoundForUnknownInteraction()
    {
        var repository = new InMemoryInteractionRepository();

        var result = await repository.TryTransitionAsync(
            "missing", AgentInteractionStatuses.Answered, "yes", "dana", InteractionChannels.Ui,
            CancellationToken.None);

        Assert.Equal(InteractionTransitionOutcome.NotFound, result.Outcome);
        Assert.Null(result.Interaction);
    }

    /// <summary>
    /// A duplicate response must not overwrite the winner. This is what stops a replayed Slack
    /// payload from rewriting who answered and via which channel.
    /// </summary>
    [Fact]
    public async Task TryTransition_DoesNotOverwriteAnAlreadyAnsweredInteraction()
    {
        var repository = new InMemoryInteractionRepository();
        var interaction = Pending("i1");
        await repository.AddAsync(interaction, CancellationToken.None);

        await repository.TryTransitionAsync(
            "i1", AgentInteractionStatuses.Answered, "approve", "dana", InteractionChannels.Slack,
            CancellationToken.None);

        var duplicate = await repository.TryTransitionAsync(
            "i1", AgentInteractionStatuses.Answered, "reject", "morgan", InteractionChannels.Ui,
            CancellationToken.None);

        Assert.Equal(InteractionTransitionOutcome.AlreadyTerminal, duplicate.Outcome);
        Assert.Equal("approve", interaction.Response);
        Assert.Equal("dana", interaction.RespondedBy);
        Assert.Equal(InteractionChannels.Slack, interaction.RespondedChannel);
        Assert.Equal(1, interaction.Version);
    }

    [Theory]
    [InlineData(AgentInteractionStatuses.Answered)]
    [InlineData(AgentInteractionStatuses.Rejected)]
    [InlineData(AgentInteractionStatuses.Expired)]
    [InlineData(AgentInteractionStatuses.Cancelled)]
    public async Task TryTransition_RefusesEveryTerminalStartingStatus(string terminal)
    {
        var repository = new InMemoryInteractionRepository();
        var interaction = Pending("i1");
        interaction.Status = terminal;
        await repository.AddAsync(interaction, CancellationToken.None);

        var result = await repository.TryTransitionAsync(
            "i1", AgentInteractionStatuses.Answered, "late", "morgan", InteractionChannels.Webhook,
            CancellationToken.None);

        Assert.Equal(InteractionTransitionOutcome.AlreadyTerminal, result.Outcome);
        Assert.Equal(terminal, interaction.Status);
    }

    /// <summary>
    /// AC 10. The UI, Slack and a webhook can answer the same question at the same instant; exactly
    /// one may win, because only the winner enqueues the outbox Resume.
    /// </summary>
    [Fact]
    public async Task TryTransition_ConcurrentCallersProduceExactlyOneWinner()
    {
        const int callers = 32;
        var repository = new InMemoryInteractionRepository();
        await repository.AddAsync(Pending("i1"), CancellationToken.None);

        using var start = new Barrier(callers);
        var results = await Task.WhenAll(Enumerable.Range(0, callers).Select(i => Task.Run(async () =>
        {
            start.SignalAndWait();
            return await repository.TryTransitionAsync(
                "i1", AgentInteractionStatuses.Answered, $"answer-{i}", $"user-{i}",
                InteractionChannels.Ui, CancellationToken.None);
        })));

        Assert.Equal(1, results.Count(r => r.Outcome == InteractionTransitionOutcome.Won));
        Assert.Equal(callers - 1, results.Count(r => r.Outcome == InteractionTransitionOutcome.AlreadyTerminal));
    }

    /// <summary>The winner's answer must be the one that survives, not the last writer's.</summary>
    [Fact]
    public async Task TryTransition_ConcurrentCallersLeaveOneCoherentAnswer()
    {
        const int callers = 16;
        var repository = new InMemoryInteractionRepository();
        var interaction = Pending("i1");
        await repository.AddAsync(interaction, CancellationToken.None);

        using var start = new Barrier(callers);
        var results = await Task.WhenAll(Enumerable.Range(0, callers).Select(i => Task.Run(async () =>
        {
            start.SignalAndWait();
            return await repository.TryTransitionAsync(
                "i1", AgentInteractionStatuses.Answered, $"answer-{i}", $"user-{i}",
                InteractionChannels.Ui, CancellationToken.None);
        })));

        var winner = results.Single(r => r.Outcome == InteractionTransitionOutcome.Won);
        Assert.Equal(winner.Interaction!.Response, interaction.Response);
        Assert.Equal(winner.Interaction.RespondedBy, interaction.RespondedBy);
        Assert.Equal(1, interaction.Version);
    }

    [Fact]
    public async Task GetDueForExpiry_ReturnsOnlyPendingRowsAtOrBeforeNow()
    {
        var repository = new InMemoryInteractionRepository();
        var due = Pending("due", timeoutAt: Iso(-5));
        var notYetDue = Pending("not-yet", timeoutAt: Iso(5));
        var noTimeout = Pending("no-timeout");
        var answered = Pending("answered", timeoutAt: Iso(-5));
        answered.Status = AgentInteractionStatuses.Answered;

        foreach (var i in new[] { due, notYetDue, noTimeout, answered })
        {
            await repository.AddAsync(i, CancellationToken.None);
        }

        var result = await repository.GetDueForExpiryAsync(Iso(0), CancellationToken.None);

        Assert.Equal(["due"], result.Select(i => i.Id));
    }

    /// <summary>
    /// A blocking question with no timeout waits indefinitely by design — the epic ships with a null
    /// default so enabling it cannot start expiring runs that previously waited forever (#221).
    /// </summary>
    [Fact]
    public async Task GetDueForExpiry_NeverReturnsRowsWithoutATimeout()
    {
        var repository = new InMemoryInteractionRepository();
        await repository.AddAsync(Pending("no-timeout"), CancellationToken.None);

        var result = await repository.GetDueForExpiryAsync(Iso(3650 * 24), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPending_FiltersByRunAndAddresseeType()
    {
        var repository = new InMemoryInteractionRepository();
        var humanAsk = Pending("human", runId: "run-1");
        var agentAsk = Pending("agent", runId: "run-1");
        agentAsk.AddresseeType = AgentInteractionAddresseeTypes.Agent;
        var otherRun = Pending("other", runId: "run-2");
        var answered = Pending("answered", runId: "run-1");
        answered.Status = AgentInteractionStatuses.Answered;

        foreach (var i in new[] { humanAsk, agentAsk, otherRun, answered })
        {
            await repository.AddAsync(i, CancellationToken.None);
        }

        var byRun = await repository.GetPendingAsync("run-1", null, CancellationToken.None);
        Assert.Equal(["human", "agent"], byRun.Select(i => i.Id));

        var byAddressee = await repository.GetPendingAsync(
            "run-1", AgentInteractionAddresseeTypes.Human, CancellationToken.None);
        Assert.Equal(["human"], byAddressee.Select(i => i.Id));

        var all = await repository.GetPendingAsync(null, null, CancellationToken.None);
        Assert.Equal(3, all.Count);
    }

    private static string Iso(int offsetMinutes) =>
        DateTimeOffset.UtcNow.AddMinutes(offsetMinutes).ToString("o");

    private static AgentInteraction Pending(string id, string runId = "run-1", string? timeoutAt = null) =>
        new()
        {
            Id = id,
            RunId = runId,
            FromAgent = "reviewer",
            Kind = AgentInteractionKinds.Question,
            AddresseeType = AgentInteractionAddresseeTypes.Human,
            Blocking = true,
            Prompt = "Deploy to production?",
            Status = AgentInteractionStatuses.Pending,
            TimeoutAt = timeoutAt,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };
}

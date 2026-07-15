using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;
using Agentwerke.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agentwerke.Api.Tests.Persistence;

/// <summary>
/// Proves the single-winner guarantee (#218) against a real Postgres, which is the only place it can
/// be proven: it depends on the database rejecting a stale write on the Version concurrency token.
/// The unit tests in Agentwerke.Agents.Tests pin the *contract* against a hand-rolled fake; they
/// cannot tell you whether EF and Postgres actually enforce it.
///
/// Each caller gets its own DbContext, which is what happens in production — a scoped context per
/// HTTP request, so a UI answer and a Slack callback genuinely race with separate change trackers.
/// Sharing one context would make every caller see the same tracked entity and the race would
/// vanish, passing for the wrong reason.
/// </summary>
public sealed class AgentInteractionConcurrencyTests : IAsyncLifetime
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable) ?? string.Empty;

    private readonly List<string> _createdIds = [];

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || _createdIds.Count == 0)
        {
            return;
        }

        await using var context = CreateContext();
        var rows = await context.AgentInteractions.Where(i => _createdIds.Contains(i.Id)).ToListAsync();
        context.AgentInteractions.RemoveRange(rows);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// AC 10. The whole epic rests on this: fan a question out to the UI and Slack, have both answer
    /// at once, and exactly one may resume the run.
    /// </summary>
    [PostgresFact]
    public async Task TryTransition_ConcurrentCallersAcrossContextsProduceExactlyOneWinner()
    {
        const int callers = 16;
        var id = await SeedPendingAsync();

        using var start = new Barrier(callers);
        var results = await Task.WhenAll(Enumerable.Range(0, callers).Select(i => Task.Run(async () =>
        {
            await using var context = CreateContext();
            var repository = new AgentInteractionRepository(context);
            start.SignalAndWait();
            return await repository.TryTransitionAsync(
                id, AgentInteractionStatuses.Answered, $"answer-{i}", $"user-{i}",
                InteractionChannels.Ui, CancellationToken.None);
        })));

        Assert.Equal(1, results.Count(r => r.Outcome == InteractionTransitionOutcome.Won));
        Assert.Equal(callers - 1, results.Count(r => r.Outcome == InteractionTransitionOutcome.AlreadyTerminal));

        // The winner's answer is the one that survived, and the token advanced exactly once.
        var winner = results.Single(r => r.Outcome == InteractionTransitionOutcome.Won).Interaction!;
        await using var verify = CreateContext();
        var stored = await verify.AgentInteractions.SingleAsync(i => i.Id == id);
        Assert.Equal(AgentInteractionStatuses.Answered, stored.Status);
        Assert.Equal(winner.Response, stored.Response);
        Assert.Equal(winner.RespondedBy, stored.RespondedBy);
        Assert.Equal(1, stored.Version);
    }

    /// <summary>
    /// A late response — a Slack click after the UI already answered — must not overwrite the winner
    /// or report success.
    /// </summary>
    [PostgresFact]
    public async Task TryTransition_LateResponseIsRejectedAndLeavesTheWinnerIntact()
    {
        var id = await SeedPendingAsync();

        await using (var first = CreateContext())
        {
            var result = await new AgentInteractionRepository(first).TryTransitionAsync(
                id, AgentInteractionStatuses.Answered, "approve", "dana", InteractionChannels.Ui,
                CancellationToken.None);
            Assert.Equal(InteractionTransitionOutcome.Won, result.Outcome);
        }

        await using (var second = CreateContext())
        {
            var result = await new AgentInteractionRepository(second).TryTransitionAsync(
                id, AgentInteractionStatuses.Answered, "reject", "morgan", InteractionChannels.Slack,
                CancellationToken.None);
            Assert.Equal(InteractionTransitionOutcome.AlreadyTerminal, result.Outcome);
        }

        await using var verify = CreateContext();
        var stored = await verify.AgentInteractions.SingleAsync(i => i.Id == id);
        Assert.Equal("approve", stored.Response);
        Assert.Equal("dana", stored.RespondedBy);
        Assert.Equal(InteractionChannels.Ui, stored.RespondedChannel);
        Assert.Equal(1, stored.Version);
    }

    /// <summary>
    /// The stale-write path specifically: two contexts both read Pending, then both write. The second
    /// must hit DbUpdateConcurrencyException internally and report AlreadyTerminal rather than
    /// throwing or silently overwriting. Without the Version token this test fails.
    /// </summary>
    [PostgresFact]
    public async Task TryTransition_StaleWriterLosesOnTheConcurrencyToken()
    {
        var id = await SeedPendingAsync();

        await using var stale = CreateContext();
        var staleRepository = new AgentInteractionRepository(stale);

        // Load the row into the stale context so its original Version is 0, then let another context
        // transition it underneath.
        await stale.AgentInteractions.SingleAsync(i => i.Id == id);

        await using (var winner = CreateContext())
        {
            await new AgentInteractionRepository(winner).TryTransitionAsync(
                id, AgentInteractionStatuses.Answered, "approve", "dana", InteractionChannels.Slack,
                CancellationToken.None);
        }

        var result = await staleRepository.TryTransitionAsync(
            id, AgentInteractionStatuses.Answered, "reject", "morgan", InteractionChannels.Ui,
            CancellationToken.None);

        Assert.Equal(InteractionTransitionOutcome.AlreadyTerminal, result.Outcome);
        Assert.Equal("approve", result.Interaction!.Response);
        Assert.Equal(InteractionChannels.Slack, result.Interaction.RespondedChannel);
    }

    [PostgresFact]
    public async Task GetDueForExpiry_ComparesIsoTimestampsChronologically()
    {
        var due = await SeedPendingAsync(timeoutAt: Iso(-5));
        var notYetDue = await SeedPendingAsync(timeoutAt: Iso(60));
        var noTimeout = await SeedPendingAsync();

        await using var context = CreateContext();
        var result = await new AgentInteractionRepository(context)
            .GetDueForExpiryAsync(Iso(0), CancellationToken.None);

        var ids = result.Select(i => i.Id).ToList();
        Assert.Contains(due, ids);
        Assert.DoesNotContain(notYetDue, ids);
        Assert.DoesNotContain(noTimeout, ids);
    }

    private async Task<string> SeedPendingAsync(string? timeoutAt = null)
    {
        var id = Guid.NewGuid().ToString("n");
        _createdIds.Add(id);

        await using var context = CreateContext();
        context.AgentInteractions.Add(new AgentInteraction
        {
            Id = id,
            RunId = $"run_{id}",
            FromAgent = "reviewer",
            Kind = AgentInteractionKinds.Question,
            AddresseeType = AgentInteractionAddresseeTypes.Human,
            Blocking = true,
            Prompt = "Deploy to production?",
            Status = AgentInteractionStatuses.Pending,
            TimeoutAt = timeoutAt,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static string Iso(int offsetMinutes) =>
        DateTimeOffset.UtcNow.AddMinutes(offsetMinutes).ToString("o");

    private AgentwerkeDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AgentwerkeDbContext>()
            .UseNpgsql(_connectionString)
            .Options);
}

using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agentwerke.Application.Tests;

/// <summary>
/// Channel resolution and fan-out (#220).
///
/// The guarantee under test is that routing is best-effort: a channel that fails, throws, or cannot
/// carry a response must leave a visible delivery row and must never surface as a failed agent step.
/// A question is answerable in the UI whatever the channels do.
/// </summary>
public sealed class InteractionRoutingTests
{
    // ── resolution ────────────────────────────────────────────────────────────

    /// <summary>The feature flag: off reproduces today's UI-only behaviour exactly.</summary>
    [Fact]
    public void Resolve_WhenDisabled_ReturnsUiOnly()
    {
        var resolver = Resolver(
            new InteractionOptions { Enabled = false, DefaultChannels = [InteractionChannels.Slack] },
            new FakeChannel(InteractionChannels.Slack));

        var resolved = resolver.Resolve(Pending(i => i.RequestedChannels = [InteractionChannels.Slack]), null);

        Assert.Equal([InteractionChannels.Ui], resolved);
    }

    /// <summary>
    /// The UI is the fallback that makes every other channel optional, so it cannot be configured away.
    /// </summary>
    [Fact]
    public void Resolve_AlwaysIncludesTheUiEvenWhenNotRequested()
    {
        var resolver = Resolver(
            Options(defaults: [InteractionChannels.Slack]),
            new FakeChannel(InteractionChannels.Slack));

        var resolved = resolver.Resolve(Pending(), null);

        Assert.Contains(InteractionChannels.Ui, resolved);
        Assert.Contains(InteractionChannels.Slack, resolved);
    }

    [Fact]
    public void Resolve_PerInteractionRequestBeatsEveryConfiguredLayer()
    {
        var options = Options(defaults: [InteractionChannels.Teams]);
        options.ChannelsByWorkflow["Deploy"] = [InteractionChannels.Webhook];
        options.ChannelsByAgent["reviewer"] = [InteractionChannels.Teams];

        var resolver = Resolver(options,
            new FakeChannel(InteractionChannels.Slack),
            new FakeChannel(InteractionChannels.Teams),
            new FakeChannel(InteractionChannels.Webhook));

        var resolved = resolver.Resolve(
            Pending(i => i.RequestedChannels = [InteractionChannels.Slack]), "Deploy");

        Assert.Equal([InteractionChannels.Ui, InteractionChannels.Slack], resolved);
    }

    [Fact]
    public void Resolve_WorkflowConfigBeatsAgentConfigAndDefault()
    {
        var options = Options(defaults: [InteractionChannels.Teams]);
        options.ChannelsByWorkflow["Deploy"] = [InteractionChannels.Slack];
        options.ChannelsByAgent["reviewer"] = [InteractionChannels.Webhook];

        var resolver = Resolver(options,
            new FakeChannel(InteractionChannels.Slack),
            new FakeChannel(InteractionChannels.Teams),
            new FakeChannel(InteractionChannels.Webhook));

        Assert.Equal(
            [InteractionChannels.Ui, InteractionChannels.Slack],
            resolver.Resolve(Pending(), "Deploy"));
    }

    [Fact]
    public void Resolve_AgentConfigBeatsDefault()
    {
        var options = Options(defaults: [InteractionChannels.Teams]);
        options.ChannelsByAgent["reviewer"] = [InteractionChannels.Webhook];

        var resolver = Resolver(options,
            new FakeChannel(InteractionChannels.Teams),
            new FakeChannel(InteractionChannels.Webhook));

        Assert.Equal(
            [InteractionChannels.Ui, InteractionChannels.Webhook],
            resolver.Resolve(Pending(), "Unconfigured"));
    }

    [Fact]
    public void Resolve_DropsUnknownAndDisabledChannels()
    {
        var resolver = Resolver(Options(),
            new FakeChannel(InteractionChannels.Slack, enabled: false));

        var resolved = resolver.Resolve(
            Pending(i => i.RequestedChannels = [InteractionChannels.Slack, "carrier-pigeon"]), null);

        Assert.Equal([InteractionChannels.Ui], resolved);
    }

    [Fact]
    public void Resolve_DoesNotDuplicateARepeatedChannel()
    {
        var resolver = Resolver(Options(), new FakeChannel(InteractionChannels.Slack));

        var resolved = resolver.Resolve(
            Pending(i => i.RequestedChannels = [InteractionChannels.Slack, InteractionChannels.Slack, InteractionChannels.Ui]),
            null);

        Assert.Equal([InteractionChannels.Ui, InteractionChannels.Slack], resolved);
    }

    // ── routing ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Route_DeliversToEachResolvedChannelAndRecordsARow()
    {
        var slack = new FakeChannel(InteractionChannels.Slack, result: InteractionDeliveryResult.Delivered("ts-1"));
        var harness = Harness.For(Options(), slack);

        await harness.Router.RouteAsync(harness.Interaction, CancellationToken.None);

        Assert.Equal(1, slack.Calls);
        var rows = await harness.Deliveries.GetByInteractionAsync("int_1", CancellationToken.None);
        Assert.Equal(2, rows.Count);
        Assert.Equal(InteractionDeliveryStatuses.Delivered, Row(rows, InteractionChannels.Ui).Status);

        var slackRow = Row(rows, InteractionChannels.Slack);
        Assert.Equal(InteractionDeliveryStatuses.Delivered, slackRow.Status);
        Assert.Equal("ts-1", slackRow.ChannelMessageId);
    }

    /// <summary>
    /// The UI needs no delivery — the persisted row is what it reads — but it still gets a delivery
    /// row so every surface can render "available in: ui, slack" uniformly.
    /// </summary>
    [Fact]
    public async Task Route_RecordsTheUiWithoutCallingAnyChannel()
    {
        var harness = Harness.For(new InteractionOptions { Enabled = false });

        await harness.Router.RouteAsync(harness.Interaction, CancellationToken.None);

        var rows = await harness.Deliveries.GetByInteractionAsync("int_1", CancellationToken.None);
        Assert.Equal(InteractionChannels.Ui, Assert.Single(rows).Channel);
    }

    /// <summary>AC 14. A failed delivery is recorded with its attempts and error, and stays retryable.</summary>
    [Fact]
    public async Task Route_FailedDeliveryIsRecordedWithAttemptsAndError()
    {
        var slack = new FakeChannel(InteractionChannels.Slack, result: InteractionDeliveryResult.Failed("503 from Slack"));
        var harness = Harness.For(Options(maxAttempts: 3), slack);

        await harness.Router.RouteAsync(harness.Interaction, CancellationToken.None);

        Assert.Equal(3, slack.Calls);
        var row = Row(await harness.Deliveries.GetByInteractionAsync("int_1", CancellationToken.None), InteractionChannels.Slack);
        Assert.Equal(InteractionDeliveryStatuses.Failed, row.Status);
        Assert.Equal(3, row.Attempts);
        Assert.Equal("503 from Slack", row.LastError);
        Assert.NotNull(row.LastAttemptAt);
    }

    [Fact]
    public async Task Route_StopsRetryingOnceAChannelSucceeds()
    {
        var slack = new FakeChannel(InteractionChannels.Slack, resultsInOrder:
        [
            InteractionDeliveryResult.Failed("transient"),
            InteractionDeliveryResult.Delivered("ts-2"),
        ]);
        var harness = Harness.For(Options(maxAttempts: 3), slack);

        await harness.Router.RouteAsync(harness.Interaction, CancellationToken.None);

        Assert.Equal(2, slack.Calls);
        Assert.Equal(
            InteractionDeliveryStatuses.Delivered,
            Row(await harness.Deliveries.GetByInteractionAsync("int_1", CancellationToken.None), InteractionChannels.Slack).Status);
    }

    /// <summary>
    /// The load-bearing guarantee: a Slack outage must never fail the agent step that asked the
    /// question. Adapters should return Failed, but the router must not rely on their discipline.
    /// </summary>
    [Fact]
    public async Task Route_AChannelThatThrowsNeverPropagates()
    {
        var slack = new FakeChannel(InteractionChannels.Slack, throws: new HttpRequestException("network down"));
        var harness = Harness.For(Options(maxAttempts: 2), slack);

        await harness.Router.RouteAsync(harness.Interaction, CancellationToken.None);

        var row = Row(await harness.Deliveries.GetByInteractionAsync("int_1", CancellationToken.None), InteractionChannels.Slack);
        Assert.Equal(InteractionDeliveryStatuses.Failed, row.Status);
        Assert.Contains("network down", row.LastError);
    }

    [Fact]
    public async Task Route_ARepositoryFailureNeverPropagates()
    {
        var harness = Harness.For(Options(), new FakeChannel(InteractionChannels.Slack));
        harness.Deliveries.ThrowOnUpsert = true;

        // No assertion beyond "does not throw": routing is best-effort, and the interaction is already
        // persisted and answerable in the UI.
        await harness.Router.RouteAsync(harness.Interaction, CancellationToken.None);
    }

    /// <summary>
    /// AC 4. Teams is outbound-only in v1, so a blocking question must not silently land somewhere
    /// nobody can answer from.
    /// </summary>
    [Fact]
    public async Task Route_BlockingInteractionOnAOneWayChannelIsRecordedNotSupported()
    {
        var teams = new FakeChannel(InteractionChannels.Teams, canCarryResponse: false);
        var harness = Harness.For(Options(defaults: [InteractionChannels.Teams]), teams);

        await harness.Router.RouteAsync(harness.Interaction, CancellationToken.None);

        Assert.Equal(0, teams.Calls);
        var row = Row(await harness.Deliveries.GetByInteractionAsync("int_1", CancellationToken.None), InteractionChannels.Teams);
        Assert.Equal(InteractionDeliveryStatuses.NotSupported, row.Status);
        Assert.Contains("cannot accept responses", row.LastError);
    }

    /// <summary>A one-way channel is perfectly good for a notification: nothing needs to come back.</summary>
    [Fact]
    public async Task Route_NonBlockingNotifyOnAOneWayChannelIsDelivered()
    {
        var teams = new FakeChannel(InteractionChannels.Teams, canCarryResponse: false);
        var harness = Harness.For(Options(defaults: [InteractionChannels.Teams]), teams,
            configure: i =>
            {
                i.Blocking = false;
                i.Kind = AgentInteractionKinds.Notify;
            });

        await harness.Router.RouteAsync(harness.Interaction, CancellationToken.None);

        Assert.Equal(1, teams.Calls);
        Assert.Equal(
            InteractionDeliveryStatuses.Delivered,
            Row(await harness.Deliveries.GetByInteractionAsync("int_1", CancellationToken.None), InteractionChannels.Teams).Status);
    }

    // ── retry ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_ReattemptsAndUpdatesTheExistingRow()
    {
        var slack = new FakeChannel(InteractionChannels.Slack, resultsInOrder:
        [
            InteractionDeliveryResult.Failed("503"),
            InteractionDeliveryResult.Delivered("ts-9"),
        ]);
        var harness = Harness.For(Options(maxAttempts: 1), slack);

        await harness.Router.RouteAsync(harness.Interaction, CancellationToken.None);
        Assert.Equal(
            InteractionDeliveryStatuses.Failed,
            Row(await harness.Deliveries.GetByInteractionAsync("int_1", CancellationToken.None), InteractionChannels.Slack).Status);

        var result = await harness.Router.RetryAsync("int_1", InteractionChannels.Slack, CancellationToken.None);

        Assert.Equal(InteractionDeliveryStatuses.Delivered, result.Status);
        var rows = await harness.Deliveries.GetByInteractionAsync("int_1", CancellationToken.None);
        Assert.Equal(InteractionDeliveryStatuses.Delivered, Row(rows, InteractionChannels.Slack).Status);
        Assert.Single(rows, r => r.Channel == InteractionChannels.Slack);
    }

    /// <summary>Re-posting a settled question would show a human a message nobody can act on.</summary>
    [Fact]
    public async Task Retry_RefusesOnceTheInteractionIsTerminal()
    {
        var slack = new FakeChannel(InteractionChannels.Slack);
        var harness = Harness.For(Options(), slack, configure: i => i.Status = AgentInteractionStatuses.Answered);

        var result = await harness.Router.RetryAsync("int_1", InteractionChannels.Slack, CancellationToken.None);

        Assert.Equal(InteractionDeliveryStatuses.Failed, result.Status);
        Assert.Equal(0, slack.Calls);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static InteractionDelivery Row(IReadOnlyList<InteractionDelivery> rows, string channel) =>
        rows.Single(r => string.Equals(r.Channel, channel, StringComparison.OrdinalIgnoreCase));

    private static InteractionOptions Options(List<string>? defaults = null, int maxAttempts = 1) =>
        new()
        {
            Enabled = true,
            DefaultChannels = defaults ?? [InteractionChannels.Ui, InteractionChannels.Slack],
            MaxDeliveryAttempts = maxAttempts,
            RetryBaseDelayMs = 0,
        };

    private static InteractionChannelResolver Resolver(InteractionOptions options, params IInteractionChannel[] channels) =>
        new(channels, options, NullLogger<InteractionChannelResolver>.Instance);

    private static AgentInteraction Pending(Action<AgentInteraction>? configure = null)
    {
        var interaction = new AgentInteraction
        {
            Id = "int_1",
            RunId = "run_1",
            FromAgent = "reviewer",
            Kind = AgentInteractionKinds.Question,
            AddresseeType = AgentInteractionAddresseeTypes.Human,
            Blocking = true,
            Prompt = "Deploy to production?",
            Status = AgentInteractionStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };
        configure?.Invoke(interaction);
        return interaction;
    }

    private sealed class Harness
    {
        public required InteractionRouter Router { get; init; }
        public required AgentInteraction Interaction { get; init; }
        public required FakeDeliveryRepository Deliveries { get; init; }

        public static Harness For(
            InteractionOptions options,
            IInteractionChannel? channel = null,
            Action<AgentInteraction>? configure = null)
        {
            var interaction = Pending(configure);
            var channels = channel is null ? Array.Empty<IInteractionChannel>() : [channel];
            var deliveries = new FakeDeliveryRepository();

            return new Harness
            {
                Interaction = interaction,
                Deliveries = deliveries,
                Router = new InteractionRouter(
                    channels,
                    new InteractionChannelResolver(channels, options, NullLogger<InteractionChannelResolver>.Instance),
                    deliveries,
                    new InMemoryAgentInteractionRepository(interaction),
                    new InMemoryWorkflowRunRepository(new WorkflowRun
                    {
                        Id = "run_1", WorkflowName = "Deploy", Status = "waiting_user",
                    }),
                    options,
                    NullLogger<InteractionRouter>.Instance),
            };
        }
    }

    private sealed class FakeChannel : IInteractionChannel
    {
        private readonly InteractionDeliveryResult? _result;
        private readonly IReadOnlyList<InteractionDeliveryResult>? _resultsInOrder;
        private readonly Exception? _throws;

        public FakeChannel(
            string channelId,
            bool enabled = true,
            bool canCarryResponse = true,
            InteractionDeliveryResult? result = null,
            IReadOnlyList<InteractionDeliveryResult>? resultsInOrder = null,
            Exception? throws = null)
        {
            ChannelId = channelId;
            Enabled = enabled;
            CanCarryResponse = canCarryResponse;
            _result = result;
            _resultsInOrder = resultsInOrder;
            _throws = throws;
        }

        public string ChannelId { get; }
        public bool Enabled { get; }
        public bool CanCarryResponse { get; }
        public int Calls { get; private set; }
        public InteractionDeliveryRequest? LastRequest { get; private set; }

        public Task<InteractionDeliveryResult> DeliverAsync(
            InteractionDeliveryRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            Calls++;

            if (_throws is not null)
            {
                throw _throws;
            }

            if (_resultsInOrder is not null)
            {
                return Task.FromResult(_resultsInOrder[Math.Min(Calls - 1, _resultsInOrder.Count - 1)]);
            }

            return Task.FromResult(_result ?? InteractionDeliveryResult.Delivered("msg-1"));
        }
    }

    private sealed class FakeDeliveryRepository : IInteractionDeliveryRepository
    {
        private readonly List<InteractionDelivery> _rows = [];

        public bool ThrowOnUpsert { get; set; }

        public Task UpsertAsync(InteractionDelivery delivery, CancellationToken cancellationToken)
        {
            if (ThrowOnUpsert)
            {
                throw new InvalidOperationException("delivery store unavailable");
            }

            // Mirrors the unique (InteractionId, Channel) index the real store keys on.
            var existing = _rows.FirstOrDefault(r =>
                r.InteractionId == delivery.InteractionId && r.Channel == delivery.Channel);

            if (existing is null)
            {
                _rows.Add(delivery);
                return Task.CompletedTask;
            }

            existing.Status = delivery.Status;
            existing.Attempts = delivery.Attempts;
            existing.LastAttemptAt = delivery.LastAttemptAt;
            existing.LastError = delivery.LastError;
            if (delivery.ChannelMessageId is not null)
            {
                existing.ChannelMessageId = delivery.ChannelMessageId;
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InteractionDelivery>> GetByInteractionAsync(
            string interactionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InteractionDelivery>>(
                _rows.Where(r => r.InteractionId == interactionId).ToList());

        public Task<InteractionDelivery?> GetByChannelMessageAsync(
            string channel, string channelMessageId, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(r =>
                r.Channel == channel && r.ChannelMessageId == channelMessageId));
    }
}

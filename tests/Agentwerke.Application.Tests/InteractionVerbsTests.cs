using Agentwerke.Application.Agents;
using Agentwerke.Application.Observability;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agentwerke.Application.Tests;

/// <summary>
/// The interaction verbs (#219) built on the single-winner transition (#218).
///
/// The property under test throughout is that a run resumes exactly once: the outbox Resume is
/// enqueued only for the caller that won the transition. Nearly every test here asserts on
/// <c>outbox.Enqueued</c> for that reason — a verb that transitions correctly but resumes twice is
/// the bug this epic exists to prevent.
/// </summary>
public sealed class InteractionVerbsTests
{
    // ── answer ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Answer_RecordsTheWinningChannel()
    {
        var harness = Harness.WithPending();

        var result = await harness.Service.AnswerInteractionAsync(new AnswerInteractionCommand(
            "run_1", "int_1", "add tests", "dana", InteractionChannels.Slack));

        Assert.Equal(InteractionChannels.Slack, result.AcceptedChannel);
        Assert.Equal(AgentInteractionStatuses.Answered, harness.Interaction.Status);
        Assert.Equal(InteractionChannels.Slack, harness.Interaction.RespondedChannel);
        Assert.Single(harness.Outbox.Enqueued);
        Assert.Contains(harness.Audit.Records, r => r.Action == "interaction.answer");
    }

    [Fact]
    public async Task Answer_DefaultsToTheUiChannel()
    {
        var harness = Harness.WithPending();

        await harness.Service.AnswerInteractionAsync(
            new AnswerInteractionCommand("run_1", "int_1", "add tests", "dana"));

        Assert.Equal(InteractionChannels.Ui, harness.Interaction.RespondedChannel);
    }

    /// <summary>AC 9. A replayed response must not resume the run a second time.</summary>
    [Fact]
    public async Task Answer_DuplicateResponseDoesNotResumeAgain()
    {
        var harness = Harness.WithPending();

        await harness.Service.AnswerInteractionAsync(
            new AnswerInteractionCommand("run_1", "int_1", "approve", "dana", InteractionChannels.Ui));

        var duplicate = await Assert.ThrowsAsync<InteractionNotPendingException>(() =>
            harness.Service.AnswerInteractionAsync(
                new AnswerInteractionCommand("run_1", "int_1", "approve", "dana", InteractionChannels.Ui)));

        Assert.Equal(AgentInteractionStatuses.Answered, duplicate.Status);
        Assert.Single(harness.Outbox.Enqueued);
    }

    /// <summary>
    /// AC 10. The loser learns which channel won, so the UI can say "already answered via Slack"
    /// instead of showing an error (#228).
    /// </summary>
    [Fact]
    public async Task Answer_LoserIsToldWhichChannelWon()
    {
        var harness = Harness.WithPending();

        await harness.Service.AnswerInteractionAsync(
            new AnswerInteractionCommand("run_1", "int_1", "approve", "dana", InteractionChannels.Slack));

        var loser = await Assert.ThrowsAsync<InteractionNotPendingException>(() =>
            harness.Service.AnswerInteractionAsync(
                new AnswerInteractionCommand("run_1", "int_1", "reject", "morgan", InteractionChannels.Ui)));

        Assert.Equal(InteractionChannels.Slack, loser.RespondedChannel);
        Assert.Contains("already answered via slack", loser.Message, StringComparison.OrdinalIgnoreCase);

        // The winner's answer survives untouched, and the run resumed once.
        Assert.Equal("approve", harness.Interaction.Response);
        Assert.Equal("dana", harness.Interaction.RespondedBy);
        Assert.Single(harness.Outbox.Enqueued);
    }

    [Fact]
    public async Task Answer_LostRaceIsCountedAsANonWinningTransition()
    {
        var harness = Harness.WithPending();

        await harness.Service.AnswerInteractionAsync(
            new AnswerInteractionCommand("run_1", "int_1", "approve", "dana", InteractionChannels.Slack));
        await Assert.ThrowsAsync<InteractionNotPendingException>(() =>
            harness.Service.AnswerInteractionAsync(
                new AnswerInteractionCommand("run_1", "int_1", "reject", "morgan", InteractionChannels.Ui)));

        Assert.Contains(harness.Metrics.InteractionTransitions, t => t.Won && t.Channel == InteractionChannels.Slack);
        Assert.Contains(harness.Metrics.InteractionTransitions, t => !t.Won && t.Channel == InteractionChannels.Ui);
    }

    /// <summary>AC 11. An answer outside the offered choices is rejected before anything transitions.</summary>
    [Fact]
    public async Task Answer_RejectsAChoiceThatWasNotOffered()
    {
        var harness = Harness.WithPending(configure: i => i.Options = ["approve", "reject"]);

        var ex = await Assert.ThrowsAsync<InvalidInteractionAnswerException>(() =>
            harness.Service.AnswerInteractionAsync(
                new AnswerInteractionCommand("run_1", "int_1", "maybe", "dana")));

        Assert.Contains("approve, reject", ex.Message);
        Assert.Equal(AgentInteractionStatuses.Pending, harness.Interaction.Status);
        Assert.Empty(harness.Outbox.Enqueued);
    }

    [Fact]
    public async Task Answer_AcceptsAnOfferedChoiceCaseInsensitively()
    {
        var harness = Harness.WithPending(configure: i => i.Options = ["approve", "reject"]);

        await harness.Service.AnswerInteractionAsync(
            new AnswerInteractionCommand("run_1", "int_1", "APPROVE", "dana"));

        Assert.Equal(AgentInteractionStatuses.Answered, harness.Interaction.Status);
    }

    [Fact]
    public async Task Answer_FreeTextIsUnconstrainedWhenNoChoicesAreOffered()
    {
        var harness = Harness.WithPending();

        await harness.Service.AnswerInteractionAsync(
            new AnswerInteractionCommand("run_1", "int_1", "anything at all", "dana"));

        Assert.Equal(AgentInteractionStatuses.Answered, harness.Interaction.Status);
    }

    [Fact]
    public async Task Answer_RefusesAnInteractionBelongingToAnotherRun()
    {
        var harness = Harness.WithPending();

        await Assert.ThrowsAsync<InteractionNotFoundException>(() =>
            harness.Service.AnswerInteractionAsync(
                new AnswerInteractionCommand("run_other", "int_1", "approve", "dana")));

        Assert.Empty(harness.Outbox.Enqueued);
    }

    /// <summary>AC 15. A response landing after the run was cancelled must not resume it.</summary>
    [Theory]
    [InlineData("cancelled")]
    [InlineData("completed")]
    [InlineData("failed")]
    public async Task Answer_RefusesWhenTheRunHasAlreadyFinished(string runStatus)
    {
        var harness = Harness.WithPending(runStatus: runStatus);

        var ex = await Assert.ThrowsAsync<RunNotAcceptingResponsesException>(() =>
            harness.Service.AnswerInteractionAsync(
                new AnswerInteractionCommand("run_1", "int_1", "approve", "dana")));

        Assert.Equal(runStatus, ex.Status);
        Assert.Equal(AgentInteractionStatuses.Pending, harness.Interaction.Status);
        Assert.Empty(harness.Outbox.Enqueued);
    }

    // ── reject ────────────────────────────────────────────────────────────────

    /// <summary>
    /// AC 2. Rejection resumes the run: the step must re-run so the tool can fail it with the reason
    /// (#222). "Reject" is not "do nothing".
    /// </summary>
    [Fact]
    public async Task Reject_MarksRejectedAndResumesSoTheStepCanFail()
    {
        var harness = Harness.WithPending(configure: i => i.Kind = AgentInteractionKinds.Confirm);

        var result = await harness.Service.RejectInteractionAsync(new RejectInteractionCommand(
            "run_1", "int_1", "Not during the freeze.", "dana", InteractionChannels.Ui));

        Assert.Equal(AgentInteractionStatuses.Rejected, harness.Interaction.Status);
        Assert.Equal("Not during the freeze.", harness.Interaction.Response);
        Assert.Equal("dana", harness.Interaction.RespondedBy);
        Assert.Equal(InteractionChannels.Ui, result.AcceptedChannel);
        Assert.Single(harness.Outbox.Enqueued);
        Assert.Contains(harness.Audit.Records, r => r.Action == "interaction.reject");
    }

    [Fact]
    public async Task Reject_AfterAnAnswerLosesAndDoesNotResumeAgain()
    {
        var harness = Harness.WithPending();

        await harness.Service.AnswerInteractionAsync(
            new AnswerInteractionCommand("run_1", "int_1", "approve", "dana"));

        await Assert.ThrowsAsync<InteractionNotPendingException>(() =>
            harness.Service.RejectInteractionAsync(
                new RejectInteractionCommand("run_1", "int_1", "too late", "morgan")));

        Assert.Equal(AgentInteractionStatuses.Answered, harness.Interaction.Status);
        Assert.Single(harness.Outbox.Enqueued);
    }

    // ── cancel ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_MarksCancelledWithActorAndResumesTheBlockedStep()
    {
        var harness = Harness.WithPending();

        var result = await harness.Service.CancelInteractionAsync(
            new CancelInteractionCommand("int_1", "Superseded.", "reviewer-agent"));

        Assert.Equal("cancelled", result.Status);
        Assert.Equal(AgentInteractionStatuses.Cancelled, harness.Interaction.Status);
        Assert.Equal("reviewer-agent", harness.Interaction.CancelledBy);
        Assert.NotNull(harness.Interaction.CancelledAt);
        Assert.Single(harness.Outbox.Enqueued);
        Assert.Contains(harness.Audit.Records, r => r.Action == "interaction.cancel");
    }

    /// <summary>
    /// A non-blocking interaction has no parked step. Resuming would advance the run a second time,
    /// so cancelling a notification must transition it and stop there.
    /// </summary>
    [Fact]
    public async Task Cancel_DoesNotResumeANonBlockingInteraction()
    {
        var harness = Harness.WithPending(configure: i =>
        {
            i.Blocking = false;
            i.Kind = AgentInteractionKinds.Notify;
            i.Status = AgentInteractionStatuses.Pending;
        });

        await harness.Service.CancelInteractionAsync(
            new CancelInteractionCommand("int_1", "Superseded.", "reviewer-agent"));

        Assert.Equal(AgentInteractionStatuses.Cancelled, harness.Interaction.Status);
        Assert.Empty(harness.Outbox.Enqueued);
    }

    [Fact]
    public async Task Cancel_AfterAnAnswerLosesAndLeavesTheAnswerIntact()
    {
        var harness = Harness.WithPending();

        await harness.Service.AnswerInteractionAsync(
            new AnswerInteractionCommand("run_1", "int_1", "approve", "dana"));

        await Assert.ThrowsAsync<InteractionNotPendingException>(() =>
            harness.Service.CancelInteractionAsync(
                new CancelInteractionCommand("int_1", "too late", "operator")));

        Assert.Equal(AgentInteractionStatuses.Answered, harness.Interaction.Status);
        Assert.Null(harness.Interaction.CancelledAt);
        Assert.Single(harness.Outbox.Enqueued);
    }

    // ── expire ────────────────────────────────────────────────────────────────

    /// <summary>AC 12. fail resumes with no answer; the tool fails the step on re-run (#222).</summary>
    [Fact]
    public async Task Expire_WithFailActionResumesWithoutAnAnswer()
    {
        var harness = Harness.WithPending(configure: i => i.ExpiresAction = InteractionExpiryActions.Fail);

        var result = await harness.Service.ExpireInteractionAsync("int_1");

        Assert.Equal("expired", result.Status);
        Assert.Equal(AgentInteractionStatuses.Expired, harness.Interaction.Status);
        Assert.Null(harness.Interaction.Response);
        Assert.Single(harness.Outbox.Enqueued);
        Assert.Contains(harness.Audit.Records, r => r.Action == "interaction.expire");
    }

    [Fact]
    public async Task Expire_WithDefaultAnswerResumesCarryingThatAnswer()
    {
        var harness = Harness.WithPending(configure: i =>
        {
            i.ExpiresAction = InteractionExpiryActions.DefaultAnswer;
            i.DefaultAnswer = "reject";
        });

        await harness.Service.ExpireInteractionAsync("int_1");

        Assert.Equal(AgentInteractionStatuses.Expired, harness.Interaction.Status);
        Assert.Equal("reject", harness.Interaction.Response);
        Assert.Single(harness.Outbox.Enqueued);
    }

    /// <summary>
    /// The sweeper races the responder through the same transition. Losing is the mechanism working,
    /// not an error — but it must not resume the run a second time.
    /// </summary>
    [Fact]
    public async Task Expire_LosingToAnAnswerDoesNotResumeAgain()
    {
        var harness = Harness.WithPending();

        await harness.Service.AnswerInteractionAsync(
            new AnswerInteractionCommand("run_1", "int_1", "approve", "dana"));

        await Assert.ThrowsAsync<InteractionNotPendingException>(() =>
            harness.Service.ExpireInteractionAsync("int_1"));

        Assert.Equal(AgentInteractionStatuses.Answered, harness.Interaction.Status);
        Assert.Equal("approve", harness.Interaction.Response);
        Assert.Single(harness.Outbox.Enqueued);
    }

    [Fact]
    public async Task Expire_AuditsTheSweeperAsASystemActor()
    {
        var harness = Harness.WithPending();

        await harness.Service.ExpireInteractionAsync("int_1");

        var record = Assert.Single(harness.Audit.Records, r => r.Action == "interaction.expire");
        Assert.Equal("system", record.ActorType);
    }

    // ── harness ───────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public required WorkflowRunOrchestrationService Service { get; init; }
        public required AgentInteraction Interaction { get; init; }
        public required CapturingRunOutbox Outbox { get; init; }
        public required CapturingAuditRepository Audit { get; init; }
        public required NoOpWorkflowMetrics Metrics { get; init; }

        public static Harness WithPending(
            string runStatus = "waiting_user",
            Action<AgentInteraction>? configure = null)
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

            var outbox = new CapturingRunOutbox();
            var audit = new CapturingAuditRepository();
            var metrics = new NoOpWorkflowMetrics();

            return new Harness
            {
                Interaction = interaction,
                Outbox = outbox,
                Audit = audit,
                Metrics = metrics,
                Service = new WorkflowRunOrchestrationService(
                    new UnusedWorkflowDefinitionRepository(),
                    new InMemoryWorkflowRunRepository(new WorkflowRun { Id = "run_1", Status = runStatus }),
                    new NoOpRunContextRepository(),
                    new InMemoryApprovalRepository(new ApprovalRequest
                    {
                        Id = "unused", RunId = "unused", Status = "pending", RiskLevel = "low",
                    }),
                    new InMemoryAgentInteractionRepository(interaction),
                    audit,
                    outbox,
                    new StubCorrelationContext("corr_verbs"),
                    metrics,
                    NullLogger<WorkflowRunOrchestrationService>.Instance),
            };
        }
    }
}

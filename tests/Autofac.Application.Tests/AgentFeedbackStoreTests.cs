using Autofac.Application.Agents;

namespace Autofac.Application.Tests;

public sealed class AgentFeedbackStoreTests
{
    private static AgentFeedback Feedback(string agent, string signal) =>
        new(agent, "run-1", "approval", signal, null, DateTimeOffset.UtcNow.ToString("o"));

    [Fact]
    public void Scorecard_AggregatesSignalsAndApprovalRate()
    {
        var store = new InMemoryAgentFeedbackStore();
        store.Record(Feedback("ba-agent", "approve"));
        store.Record(Feedback("ba-agent", "approve"));
        store.Record(Feedback("ba-agent", "reject"));
        store.Record(Feedback("ba-agent", "escalate"));

        var card = store.Scorecard("ba-agent");

        Assert.Equal(4, card.Total);
        Assert.Equal(2, card.Approvals);
        Assert.Equal(1, card.Rejections);
        Assert.Equal(1, card.Escalations);
        Assert.Equal(2.0 / 3.0, card.ApprovalRate, 3); // approvals / (approvals + rejections)
    }

    [Fact]
    public void Scorecard_UnknownAgent_IsAllZero()
    {
        var card = new InMemoryAgentFeedbackStore().Scorecard("nobody");
        Assert.Equal(0, card.Total);
        Assert.Equal(0, card.ApprovalRate);
    }

    [Fact]
    public void Record_KeepsAgentsIsolated()
    {
        var store = new InMemoryAgentFeedbackStore();
        store.Record(Feedback("a", "approve"));
        store.Record(Feedback("b", "reject"));

        Assert.Equal(1, store.Scorecard("a").Approvals);
        Assert.Equal(0, store.Scorecard("a").Rejections);
        Assert.Equal(1, store.Scorecard("b").Rejections);
    }

    [Fact]
    public void Record_IgnoresEmptyAgentName()
    {
        var store = new InMemoryAgentFeedbackStore();
        store.Record(Feedback(string.Empty, "approve"));
        Assert.Empty(store.ForAgent(string.Empty));
    }
}

using System.Collections.Concurrent;

namespace Autofac.Application.Agents;

/// <summary>One feedback signal about an agent's behaviour on a run (#177).</summary>
public sealed record AgentFeedback(
    string AgentName,
    string RunId,
    string Kind,    // "approval" | "rating" | "outcome"
    string Signal,  // approve|reject|escalate | positive|negative | completed|failed
    string? Comment,
    string RecordedAt);

/// <summary>Aggregated feedback for one agent, the basis of a feedback-driven scorecard (#177).</summary>
public sealed record AgentScorecard(
    string AgentName,
    int Total,
    int Approvals,
    int Rejections,
    int Escalations,
    int Positive,
    int Negative,
    double ApprovalRate);

/// <summary>
/// Captures and aggregates per-agent feedback so it can inform reviewed improvement proposals
/// (#177). In-memory today; persistence alongside run history is a follow-up.
/// </summary>
public interface IAgentFeedbackStore
{
    void Record(AgentFeedback feedback);

    IReadOnlyList<AgentFeedback> ForAgent(string agentName);

    AgentScorecard Scorecard(string agentName);
}

public sealed class InMemoryAgentFeedbackStore : IAgentFeedbackStore
{
    private readonly ConcurrentDictionary<string, List<AgentFeedback>> _byAgent =
        new(StringComparer.OrdinalIgnoreCase);

    public void Record(AgentFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        if (string.IsNullOrWhiteSpace(feedback.AgentName))
        {
            return; // nothing to attribute the feedback to
        }

        var list = _byAgent.GetOrAdd(feedback.AgentName, static _ => []);
        lock (list)
        {
            list.Add(feedback);
        }
    }

    public IReadOnlyList<AgentFeedback> ForAgent(string agentName)
    {
        if (!_byAgent.TryGetValue(agentName, out var list))
        {
            return [];
        }

        lock (list)
        {
            return list.ToArray();
        }
    }

    public AgentScorecard Scorecard(string agentName)
    {
        var items = ForAgent(agentName);

        var approvals = items.Count(f => Is(f, "approve"));
        var rejections = items.Count(f => Is(f, "reject"));
        var escalations = items.Count(f => Is(f, "escalate"));
        var positive = items.Count(f => Is(f, "positive"));
        var negative = items.Count(f => Is(f, "negative"));

        var decided = approvals + rejections;
        var approvalRate = decided == 0 ? 0d : (double)approvals / decided;

        return new AgentScorecard(
            agentName, items.Count, approvals, rejections, escalations, positive, negative, approvalRate);
    }

    private static bool Is(AgentFeedback feedback, string signal) =>
        string.Equals(feedback.Signal, signal, StringComparison.OrdinalIgnoreCase);
}

using Agentwerke.Domain.Persistence;

namespace Agentwerke.AgentSecOps;

/// <summary>A named evaluation scenario to replay against current and proposed policy.</summary>
public sealed record PolicySimulationScenario(string Name, PolicyEvaluationRequest Request);

/// <summary>Per-scenario comparison of the current vs proposed policy decision.</summary>
public sealed record PolicySimulationOutcome(
    string ScenarioName,
    PolicyDecision Current,
    PolicyDecision Proposed,
    bool Changed,
    IReadOnlyList<string> Changes);

/// <summary>Impact analysis of a proposed policy change across a set of scenarios.</summary>
public sealed record PolicySimulationReport(
    int ScenarioCount,
    int ChangedCount,
    IReadOnlyList<PolicySimulationOutcome> Outcomes);

/// <summary>
/// Policy-change impact analysis (#34). Replays each scenario against the current
/// and the proposed rule set and reports the decisions that would change — letting
/// an operator validate a policy change before activating it. Pure and deterministic.
/// </summary>
public static class PolicySimulator
{
    public static PolicySimulationReport Simulate(
        PolicyRuleSet current,
        PolicyRuleSet proposed,
        IReadOnlyList<PolicySimulationScenario> scenarios)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(scenarios);

        var currentService = new PolicyEvaluationService(new InMemoryPolicyRuleStore(current));
        var proposedService = new PolicyEvaluationService(new InMemoryPolicyRuleStore(proposed));

        var outcomes = new List<PolicySimulationOutcome>(scenarios.Count);
        var changedCount = 0;

        foreach (var scenario in scenarios)
        {
            var currentDecision = currentService.Evaluate(scenario.Request);
            var proposedDecision = proposedService.Evaluate(scenario.Request);
            var changes = Diff(currentDecision, proposedDecision);
            if (changes.Count > 0)
            {
                changedCount++;
            }

            outcomes.Add(new PolicySimulationOutcome(
                scenario.Name,
                currentDecision,
                proposedDecision,
                changes.Count > 0,
                changes));
        }

        return new PolicySimulationReport(scenarios.Count, changedCount, outcomes);
    }

    private static IReadOnlyList<string> Diff(PolicyDecision current, PolicyDecision proposed)
    {
        var changes = new List<string>();

        if (!string.Equals(current.Kind, proposed.Kind, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"decision: {current.Kind} → {proposed.Kind}");
        }

        if (!string.Equals(current.RiskLevel, proposed.RiskLevel, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"risk level: {current.RiskLevel} → {proposed.RiskLevel}");
        }

        if (current.RiskScore != proposed.RiskScore)
        {
            changes.Add($"risk score: {current.RiskScore} → {proposed.RiskScore}");
        }

        if (!string.Equals(current.PolicyId, proposed.PolicyId, StringComparison.Ordinal))
        {
            changes.Add($"matched policy: {current.PolicyId} → {proposed.PolicyId}");
        }

        return changes;
    }
}

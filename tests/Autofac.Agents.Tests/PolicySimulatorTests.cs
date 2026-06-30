using Autofac.AgentSecOps;

namespace Autofac.Agents.Tests;

public sealed class PolicySimulatorTests
{
    private static PolicyRuleSet RejectRuleSet() => new()
    {
        Rules =
        [
            new PolicyRule
            {
                Id = "reject-secrets",
                Name = "Reject secret access",
                Enabled = true,
                Priority = 1,
                DecisionKind = "reject",
                RiskLevel = "critical",
                RiskScore = 95,
                Predicates =
                [
                    new PolicyRulePredicate
                    {
                        Field = PolicyRuleFields.Action,
                        Match = PolicyRuleMatches.ContainsAny,
                        Values = ["secret"],
                    },
                ],
            },
        ],
    };

    private static PolicySimulationScenario Scenario(string name, string action) =>
        new(name, new PolicyEvaluationRequest(
            AgentName: "agent",
            Action: action,
            Environment: "staging",
            PurposeType: "general",
            PolicyTag: "",
            RequiresEvidence: [],
            Attempt: 1));

    [Fact]
    public void Simulate_WhenProposedRuleChangesDecision_ReportsTheChange()
    {
        var current = new PolicyRuleSet(); // no rules → default-allow
        var proposed = RejectRuleSet();

        var report = PolicySimulator.Simulate(
            current,
            proposed,
            [Scenario("secret access", "secret.export")]);

        Assert.Equal(1, report.ScenarioCount);
        Assert.Equal(1, report.ChangedCount);
        var outcome = Assert.Single(report.Outcomes);
        Assert.True(outcome.Changed);
        Assert.Equal("allow", outcome.Current.Kind);
        Assert.Equal("reject", outcome.Proposed.Kind);
        Assert.Contains(outcome.Changes, c => c.Contains("decision: allow → reject"));
    }

    [Fact]
    public void Simulate_WhenScenarioIsUnaffected_ReportsNoChange()
    {
        var current = new PolicyRuleSet();
        var proposed = RejectRuleSet();

        var report = PolicySimulator.Simulate(
            current,
            proposed,
            [Scenario("read issue", "github.read_issue")]);

        Assert.Equal(0, report.ChangedCount);
        var outcome = Assert.Single(report.Outcomes);
        Assert.False(outcome.Changed);
        Assert.Empty(outcome.Changes);
        Assert.Equal(outcome.Current.Kind, outcome.Proposed.Kind);
    }

    [Fact]
    public void Simulate_MixedScenarios_CountsOnlyChanged()
    {
        var report = PolicySimulator.Simulate(
            new PolicyRuleSet(),
            RejectRuleSet(),
            [
                Scenario("secret access", "secret.export"),
                Scenario("read issue", "github.read_issue"),
            ]);

        Assert.Equal(2, report.ScenarioCount);
        Assert.Equal(1, report.ChangedCount);
    }
}

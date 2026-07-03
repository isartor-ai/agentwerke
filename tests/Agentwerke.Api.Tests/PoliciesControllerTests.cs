using Agentwerke.AgentSecOps;
using Agentwerke.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Agentwerke.Api.Tests;

public sealed class PoliciesControllerTests
{
    [Fact]
    public void Upsert_PersistsRuleIntoStore()
    {
        var store = new InMemoryPolicyRuleStore();
        var controller = new PoliciesController(store);
        var rule = new PolicyRule
        {
            Id = "rule-custom",
            Name = "Custom Rule",
            Enabled = true,
            Priority = 30,
            DecisionKind = "escalate",
            Rationale = "Needs approval",
            RiskScore = 60,
            RiskLevel = "medium",
            Predicates =
            [
                new PolicyRulePredicate
                {
                    Field = PolicyRuleFields.Action,
                    Match = PolicyRuleMatches.ContainsAny,
                    Values = ["terraform"]
                }
            ]
        };

        var result = controller.Upsert(rule.Id, rule);

        Assert.IsType<OkObjectResult>(result);
        var stored = store.FindById(rule.Id);
        Assert.NotNull(stored);
        Assert.Equal("Custom Rule", stored!.Name);
    }

    [Fact]
    public void Unpublish_ThenPublish_TogglesRuleEnabledState()
    {
        var store = new InMemoryPolicyRuleStore();
        var controller = new PoliciesController(store);
        var ruleId = store.GetSnapshot().Rules[0].Id;

        var unpublished = Assert.IsType<OkObjectResult>(controller.Unpublish(ruleId));
        Assert.False(((PolicyRule)unpublished.Value!).Enabled);
        Assert.False(store.FindById(ruleId)!.Enabled);

        var published = Assert.IsType<OkObjectResult>(controller.Publish(ruleId));
        Assert.True(((PolicyRule)published.Value!).Enabled);
        Assert.True(store.FindById(ruleId)!.Enabled);
    }

    [Fact]
    public void Publish_UnknownRule_ReturnsNotFound()
    {
        var controller = new PoliciesController(new InMemoryPolicyRuleStore());
        Assert.IsType<NotFoundResult>(controller.Publish("does-not-exist"));
    }

    [Fact]
    public void Simulate_ReturnsImpactReport()
    {
        var controller = new PoliciesController(new InMemoryPolicyRuleStore());

        var proposed = new PolicyRuleSet
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
                            Values = ["secret"]
                        }
                    ]
                }
            ]
        };

        var request = new PolicyRuleSimulationRequest(
            ProposedRules: proposed,
            Scenarios:
            [
                new PolicyRuleSimulationScenario(
                    Name: "secret access",
                    AgentName: "agent",
                    Action: "secret.export",
                    Environment: "production",
                    PurposeType: "general",
                    PolicyTag: null,
                    RequiresEvidence: null,
                    Attempt: 1)
            ]);

        var ok = Assert.IsType<OkObjectResult>(controller.Simulate(request));
        var report = Assert.IsType<PolicySimulationReport>(ok.Value);
        Assert.Equal(1, report.ScenarioCount);
        Assert.Equal(1, report.ChangedCount);
        Assert.Equal("reject", report.Outcomes[0].Proposed.Kind);
    }

    [Fact]
    public void Simulate_WithNoScenarios_ReturnsBadRequest()
    {
        var controller = new PoliciesController(new InMemoryPolicyRuleStore());
        var request = new PolicyRuleSimulationRequest(ProposedRules: null, Scenarios: []);
        Assert.IsType<BadRequestObjectResult>(controller.Simulate(request));
    }
}

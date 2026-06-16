using Autofac.AgentSecOps;

namespace Autofac.Agents.Tests;

public sealed class PolicyRuleStoreTests
{
    [Fact]
    public void Evaluate_WhenCustomRuleMatches_UsesStoreBackedDecision()
    {
        var store = new InMemoryPolicyRuleStore(new PolicyRuleSet
        {
            Version = "test",
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o"),
            Rules =
            [
                new PolicyRule
                {
                    Id = "rule-custom-escalate",
                    Name = "Escalate Terraform",
                    Enabled = true,
                    Priority = 1,
                    DecisionKind = "escalate",
                    Rationale = "Terraform changes require review.",
                    RiskScore = 70,
                    RiskLevel = "high",
                    Predicates =
                    [
                        new PolicyRulePredicate
                        {
                            Field = PolicyRuleFields.Action,
                            Match = PolicyRuleMatches.ContainsAny,
                            Values = ["terraform"]
                        }
                    ]
                }
            ]
        });
        var service = new PolicyEvaluationService(store);

        var decision = service.Evaluate(new PolicyEvaluationRequest(
            AgentName: "infra-agent",
            Action: "terraform.apply",
            Environment: "staging",
            PurposeType: "infrastructure_change",
            PolicyTag: "infra-change",
            RequiresEvidence: [],
            Attempt: 1));

        Assert.Equal("escalate", decision.Kind);
        Assert.Equal("rule-custom-escalate", decision.PolicyId);
        Assert.Equal("Terraform changes require review.", decision.Rationale);
    }
}

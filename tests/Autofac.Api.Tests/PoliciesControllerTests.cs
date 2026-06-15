using Autofac.AgentSecOps;
using Autofac.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Tests;

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
}

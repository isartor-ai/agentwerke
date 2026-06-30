using Autofac.AgentSecOps;

namespace Autofac.Agents.Tests;

public sealed class PurposeRiskScorerTests
{
    [Fact]
    public void Score_WhenPurposeIsWellFormed_ReturnsHighConfidence()
    {
        var assessment = PurposeRiskScorer.Score(
            new PolicyEvaluationRequest(
                AgentName: "reader",
                Action: "github.read_issue",
                Environment: "staging",
                PurposeType: "read_issue",
                PolicyTag: "intake",
                RequiresEvidence: ["issue_link"],
                Attempt: 1),
            baseRiskLevel: "low");

        Assert.True(assessment.PurposeConfidence >= 80, $"confidence was {assessment.PurposeConfidence}");
        Assert.Null(assessment.EscalatedRiskLevel);
    }

    [Fact]
    public void Score_WhenPurposeMissing_ReturnsLowConfidenceAndRiskFactor()
    {
        var assessment = PurposeRiskScorer.Score(
            new PolicyEvaluationRequest(
                AgentName: "agent",
                Action: "github.read_issue",
                Environment: null,
                PurposeType: "",
                PolicyTag: "",
                RequiresEvidence: [],
                Attempt: 1),
            baseRiskLevel: "low");

        Assert.True(assessment.PurposeConfidence < 50, $"confidence was {assessment.PurposeConfidence}");
        Assert.Contains("Purpose is unspecified or generic", assessment.AdditionalRiskFactors);
    }

    [Fact]
    public void Score_WhenHighImpactActionHasLowConfidence_EscalatesRisk()
    {
        var assessment = PurposeRiskScorer.Score(
            new PolicyEvaluationRequest(
                AgentName: "agent",
                Action: "github.create_pull_request",
                Environment: null,
                PurposeType: "",
                PolicyTag: "",
                RequiresEvidence: [],
                Attempt: 1),
            baseRiskLevel: "low");

        Assert.True(assessment.PurposeConfidence < 50);
        Assert.Equal("medium", assessment.EscalatedRiskLevel);
        Assert.Contains(assessment.AdditionalRiskFactors, f => f.Contains("Low purpose confidence"));
    }

    [Fact]
    public void Score_EscalationCapsAtCritical()
    {
        var assessment = PurposeRiskScorer.Score(
            new PolicyEvaluationRequest(
                AgentName: "agent",
                Action: "secret.delete",
                Environment: "production",
                PurposeType: "",
                PolicyTag: "",
                RequiresEvidence: [],
                Attempt: 1),
            baseRiskLevel: "critical");

        // Already at the top of the ladder — no escalation possible.
        Assert.Null(assessment.EscalatedRiskLevel);
    }

    [Fact]
    public void Score_RetryAttempts_LowerConfidence()
    {
        PolicyEvaluationRequest Request(int attempt) => new(
            AgentName: "agent",
            Action: "github.read_issue",
            Environment: "staging",
            PurposeType: "read_issue",
            PolicyTag: "intake",
            RequiresEvidence: ["issue_link"],
            Attempt: attempt);

        var first = PurposeRiskScorer.Score(Request(1), "low");
        var retried = PurposeRiskScorer.Score(Request(3), "low");

        Assert.True(retried.PurposeConfidence < first.PurposeConfidence);
    }

    [Fact]
    public void Evaluate_AttachesPurposeConfidenceToEveryDecision()
    {
        var service = new PolicyEvaluationService();

        var decision = service.Evaluate(new PolicyEvaluationRequest(
            AgentName: "reader",
            Action: "github.read_issue",
            Environment: "staging",
            PurposeType: "read_issue",
            PolicyTag: "intake",
            RequiresEvidence: ["issue_link"],
            Attempt: 1));

        Assert.InRange(decision.PurposeConfidence, 0, 100);
        Assert.False(string.IsNullOrWhiteSpace(decision.PurposeRationale));
    }

    [Fact]
    public void Evaluate_DefaultAllow_HighImpactWithWeakPurpose_EscalatesRiskLevel()
    {
        var service = new PolicyEvaluationService();

        var decision = service.Evaluate(new PolicyEvaluationRequest(
            AgentName: "agent",
            Action: "github.create_pull_request",
            Environment: null,
            PurposeType: "",
            PolicyTag: "",
            RequiresEvidence: [],
            Attempt: 1));

        // Default-allow for github.create_* is "medium"; weak purpose escalates it.
        Assert.Equal("allow", decision.Kind);
        Assert.Equal("high", decision.RiskLevel);
        Assert.True(decision.RiskScore >= 70);
        Assert.Contains(decision.RiskFactors, f => f.Contains("Low purpose confidence"));
    }
}

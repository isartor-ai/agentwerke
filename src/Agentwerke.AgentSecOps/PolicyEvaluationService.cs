using Agentwerke.Domain.Persistence;

namespace Agentwerke.AgentSecOps;

public sealed record PolicyEvaluationRequest(
    string AgentName,
    string Action,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    IReadOnlyList<string> RequiresEvidence,
    int Attempt);

public interface IPolicyEvaluationService
{
    PolicyDecision Evaluate(PolicyEvaluationRequest request);
}

public sealed class PolicyEvaluationService : IPolicyEvaluationService
{
    private readonly IPolicyRuleStore _ruleStore;

    public PolicyEvaluationService()
        : this(new InMemoryPolicyRuleStore())
    {
    }

    public PolicyEvaluationService(IPolicyRuleStore ruleStore)
    {
        _ruleStore = ruleStore;
    }

    public PolicyDecision Evaluate(PolicyEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var action = request.Action.ToLowerInvariant();

        var matchingRule = _ruleStore
            .GetSnapshot()
            .Rules
            .Where(rule => rule.Enabled)
            .OrderBy(rule => rule.Priority)
            .FirstOrDefault(rule => MatchesRule(rule, request));

        if (matchingRule is not null)
        {
            return ApplyPurposeRiskScoring(
                CreateDecision(
                    matchingRule.DecisionKind,
                    matchingRule.Id,
                    matchingRule.Name,
                    matchingRule.Rationale,
                    matchingRule.RiskScore,
                    matchingRule.RiskLevel,
                    BuildRiskFactors(request, matchingRule),
                    matchingRule.Constraints),
                request);
        }

        var riskFactors = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.PolicyTag))
        {
            riskFactors.Add($"Policy tag: {request.PolicyTag}");
        }

        if (request.RequiresEvidence.Count > 0)
        {
            riskFactors.Add($"Evidence requirements: {string.Join(", ", request.RequiresEvidence)}");
        }

        return ApplyPurposeRiskScoring(
            CreateDecision(
                kind: "allow",
                policyId: "default-allow",
                policyName: "Default Allow",
                rationale: "No sensitive-action rule matched; execution may proceed under standard audit logging.",
                riskScore: action.StartsWith("github.create_", StringComparison.Ordinal) ? 28 : 18,
                riskLevel: action.StartsWith("github.create_", StringComparison.Ordinal) ? "medium" : "low",
                riskFactors: riskFactors,
                constraints:
                [
                    "Record policy decision and execution outcome in the run history."
                ]),
            request);
    }

    /// <summary>
    /// Attaches purpose confidence and a context-correlated risk assessment to a
    /// decision (#26). Escalation only ever raises risk — it never lowers an
    /// explicit rule's risk level or score.
    /// </summary>
    private static PolicyDecision ApplyPurposeRiskScoring(PolicyDecision decision, PolicyEvaluationRequest request)
    {
        var assessment = PurposeRiskScorer.Score(request, decision.RiskLevel);

        decision.PurposeConfidence = assessment.PurposeConfidence;
        decision.PurposeRationale = assessment.PurposeRationale;

        foreach (var factor in assessment.AdditionalRiskFactors)
        {
            if (!decision.RiskFactors.Contains(factor, StringComparer.OrdinalIgnoreCase))
            {
                decision.RiskFactors.Add(factor);
            }
        }

        if (assessment.EscalatedRiskLevel is { Length: > 0 } escalated)
        {
            decision.RiskLevel = escalated;
            decision.RiskScore = Math.Max(decision.RiskScore, RiskScoreFloor(escalated));
        }

        return decision;
    }

    private static int RiskScoreFloor(string riskLevel) => riskLevel.ToLowerInvariant() switch
    {
        "critical" => 90,
        "high" => 70,
        "medium" => 40,
        _ => 20,
    };

    private static bool MatchesRule(PolicyRule rule, PolicyEvaluationRequest request)
    {
        return rule.Predicates.All(predicate => MatchesPredicate(predicate, request));
    }

    private static bool MatchesPredicate(PolicyRulePredicate predicate, PolicyEvaluationRequest request)
    {
        var values = predicate.Values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();

        if (values.Length == 0)
        {
            return true;
        }

        var actual = ResolveField(predicate.Field, request);

        return predicate.Match switch
        {
            PolicyRuleMatches.EqualsAny => values.Any(expected => actual.Any(candidate =>
                string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase))),
            _ => values.Any(expected => actual.Any(candidate =>
                candidate.Contains(expected, StringComparison.OrdinalIgnoreCase)))
        };
    }

    private static PolicyDecision CreateDecision(
        string kind,
        string policyId,
        string policyName,
        string rationale,
        int riskScore,
        string riskLevel,
        IReadOnlyList<string> riskFactors,
        IReadOnlyList<string> constraints)
    {
        return new PolicyDecision
        {
            Kind = kind,
            PolicyId = policyId,
            PolicyName = policyName,
            Rationale = rationale,
            RiskScore = riskScore,
            RiskLevel = riskLevel,
            RiskFactors = riskFactors.ToList(),
            DecidedAt = DateTime.UtcNow.ToString("o"),
            Constraints = constraints.ToList()
        };
    }

    private static List<string> ResolveField(string field, PolicyEvaluationRequest request)
    {
        return field switch
        {
            PolicyRuleFields.Action => [request.Action],
            PolicyRuleFields.Environment => [request.Environment ?? string.Empty],
            PolicyRuleFields.PolicyTag => [request.PolicyTag],
            PolicyRuleFields.PurposeType => [request.PurposeType],
            PolicyRuleFields.AgentName => [request.AgentName],
            PolicyRuleFields.RequiresEvidence => [.. request.RequiresEvidence],
            PolicyRuleFields.Scope => [request.Environment ?? string.Empty, request.PolicyTag],
            _ => [request.Action, request.Environment ?? string.Empty, request.PolicyTag, request.PurposeType, request.AgentName, .. request.RequiresEvidence]
        };
    }

    private static IReadOnlyList<string> BuildRiskFactors(PolicyEvaluationRequest request, PolicyRule rule)
    {
        if (rule.RiskFactors.Count > 0)
        {
            var factors = new List<string>(rule.RiskFactors);

            if (!string.IsNullOrWhiteSpace(request.PolicyTag))
            {
                factors.Add($"Policy tag: {request.PolicyTag}");
            }

            return factors;
        }

        return
        [
            $"Action: {request.Action}",
            string.IsNullOrWhiteSpace(request.Environment) ? "Environment unspecified" : $"Environment: {request.Environment}",
            $"Policy tag: {request.PolicyTag}"
        ];
    }
}

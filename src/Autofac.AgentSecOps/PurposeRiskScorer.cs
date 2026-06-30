namespace Autofac.AgentSecOps;

/// <summary>
/// Result of correlating a stated purpose with the request's declared context (#26).
/// </summary>
public sealed record PurposeRiskAssessment(
    int PurposeConfidence,
    string PurposeRationale,
    IReadOnlyList<string> AdditionalRiskFactors,
    string? EscalatedRiskLevel);

/// <summary>
/// Verified-purpose model + risk-scoring engine. It scores how well a stated
/// purpose is corroborated by the surrounding context (action, environment,
/// policy tag, evidence requirements, attempt) and escalates risk when a
/// high-impact action is driven by a weakly-justified purpose.
///
/// Pure and deterministic so decisions are explainable and testable.
/// </summary>
public static class PurposeRiskScorer
{
    private static readonly string[] GenericPurposes =
        ["", "unspecified", "general", "unknown", "n/a", "none", "default"];

    private static readonly string[] HighImpactActionTokens =
        ["create", "delete", "remove", "deploy", "merge", "release", "push", "secret", "prod"];

    private static readonly string[] ProductionEnvironments =
        ["prod", "production", "live"];

    // low → medium → high → critical
    private static readonly string[] RiskLadder = ["low", "medium", "high", "critical"];

    private const int LowConfidenceThreshold = 50;

    public static PurposeRiskAssessment Score(PolicyEvaluationRequest request, string baseRiskLevel)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rationale = new List<string>();
        var riskFactors = new List<string>();
        var confidence = 0;

        var purpose = (request.PurposeType ?? string.Empty).Trim();
        var purposeIsSpecific = !GenericPurposes.Contains(purpose, StringComparer.OrdinalIgnoreCase);

        if (purposeIsSpecific)
        {
            confidence += 40;
            rationale.Add($"purpose '{purpose}' is specified");

            if (ActionAlignsWithPurpose(request.Action, purpose))
            {
                confidence += 25;
                rationale.Add("purpose aligns with the requested action");
            }
            else
            {
                riskFactors.Add($"Purpose '{purpose}' does not obviously align with action '{request.Action}'");
                rationale.Add("purpose does not clearly align with the action");
            }
        }
        else
        {
            rationale.Add("purpose is unspecified or generic");
            riskFactors.Add("Purpose is unspecified or generic");
        }

        if (!string.IsNullOrWhiteSpace(request.PolicyTag))
        {
            confidence += 15;
            rationale.Add("governance policy tag present");
        }

        if (request.RequiresEvidence.Count > 0)
        {
            confidence += 15;
            rationale.Add($"evidence requirements declared ({string.Join(", ", request.RequiresEvidence)})");
        }

        if (!string.IsNullOrWhiteSpace(request.Environment))
        {
            confidence += 5;
            rationale.Add("target environment declared");
        }

        if (request.Attempt > 1)
        {
            var penalty = Math.Min(30, 10 * (request.Attempt - 1));
            confidence -= penalty;
            rationale.Add($"retry attempt #{request.Attempt} lowers confidence");
            riskFactors.Add($"Repeated attempt (#{request.Attempt})");
        }

        confidence = Math.Clamp(confidence, 0, 100);

        string? escalatedLevel = null;
        if (confidence < LowConfidenceThreshold && IsHighImpact(request))
        {
            escalatedLevel = Escalate(baseRiskLevel);
            if (!string.Equals(escalatedLevel, baseRiskLevel, StringComparison.OrdinalIgnoreCase))
            {
                riskFactors.Add($"Low purpose confidence ({confidence}%) on a high-impact action");
            }
            else
            {
                escalatedLevel = null;
            }
        }

        return new PurposeRiskAssessment(
            confidence,
            $"Purpose confidence {confidence}% — {string.Join("; ", rationale)}.",
            riskFactors,
            escalatedLevel);
    }

    private static bool ActionAlignsWithPurpose(string action, string purpose)
    {
        var purposeTokens = Tokenize(purpose);
        var actionTokens = Tokenize(action);
        return purposeTokens.Any(p => p.Length >= 3 && actionTokens.Contains(p, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsHighImpact(PolicyEvaluationRequest request)
    {
        var action = (request.Action ?? string.Empty).ToLowerInvariant();
        if (HighImpactActionTokens.Any(token => action.Contains(token, StringComparison.Ordinal)))
        {
            return true;
        }

        var env = (request.Environment ?? string.Empty).Trim();
        return ProductionEnvironments.Contains(env, StringComparer.OrdinalIgnoreCase);
    }

    private static string Escalate(string level)
    {
        var index = Array.FindIndex(RiskLadder, l => string.Equals(l, level, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return level; // unknown level — leave as-is
        }

        return index >= RiskLadder.Length - 1 ? level : RiskLadder[index + 1];
    }

    private static IReadOnlyList<string> Tokenize(string value) =>
        value.Split(['.', '_', '-', ' ', '/', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

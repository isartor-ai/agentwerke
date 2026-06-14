using Autofac.Domain.Persistence;

namespace Autofac.AgentSecOps;

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
    private const string PolicyId = "mvp-sensitive-tool-actions";
    private const string PolicyName = "MVP Sensitive Tool Actions";

    public PolicyDecision Evaluate(PolicyEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var action = request.Action.ToLowerInvariant();
        var environment = request.Environment?.ToLowerInvariant() ?? string.Empty;
        var policyTag = request.PolicyTag.ToLowerInvariant();
        var purposeType = request.PurposeType.ToLowerInvariant();

        if (TouchesSecretMaterial(action, policyTag, purposeType))
        {
            return CreateDecision(
                kind: "reject",
                rationale: "Secret and credential access actions are blocked in the MVP policy layer.",
                riskScore: 95,
                riskLevel: "critical",
                riskFactors:
                [
                    "Secret or credential scope requested",
                    $"Policy tag: {request.PolicyTag}"
                ],
                constraints:
                [
                    "Use a dedicated secret-management workflow with human approval.",
                    "Do not expose secret material through agent output or artifacts."
                ]);
        }

        if (RequiresHumanApproval(action, environment, policyTag))
        {
            return CreateDecision(
                kind: "escalate",
                rationale: "This action targets a sensitive delivery path and must be approved before execution.",
                riskScore: 78,
                riskLevel: "high",
                riskFactors:
                [
                    $"Action: {request.Action}",
                    string.IsNullOrWhiteSpace(request.Environment) ? "Environment unspecified" : $"Environment: {request.Environment}",
                    $"Policy tag: {request.PolicyTag}"
                ],
                constraints:
                [
                    "Require human approval before execution.",
                    "Capture rollout and rollback evidence in the run record."
                ]);
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

        return CreateDecision(
            kind: "allow",
            rationale: "No sensitive-action rule matched; execution may proceed under standard audit logging.",
            riskScore: action.StartsWith("github.create_", StringComparison.Ordinal) ? 28 : 18,
            riskLevel: action.StartsWith("github.create_", StringComparison.Ordinal) ? "medium" : "low",
            riskFactors: riskFactors,
            constraints:
            [
                "Record policy decision and execution outcome in the run history."
            ]);
    }

    private static bool TouchesSecretMaterial(string action, string policyTag, string purposeType)
    {
        var mentionsSecret = action.Contains("secret", StringComparison.Ordinal) ||
            action.Contains("credential", StringComparison.Ordinal) ||
            policyTag.Contains("secret", StringComparison.Ordinal) ||
            policyTag.Contains("credential", StringComparison.Ordinal) ||
            purposeType.Contains("secret", StringComparison.Ordinal);

        var sensitiveVerb = action.Contains("access", StringComparison.Ordinal) ||
            action.Contains("export", StringComparison.Ordinal) ||
            action.Contains("reveal", StringComparison.Ordinal) ||
            action.Contains("retrieve", StringComparison.Ordinal) ||
            action.Contains("dump", StringComparison.Ordinal) ||
            action.Contains("rotate", StringComparison.Ordinal);

        return mentionsSecret && sensitiveVerb;
    }

    private static bool RequiresHumanApproval(string action, string environment, string policyTag)
    {
        if (action.Contains("merge", StringComparison.Ordinal))
        {
            return true;
        }

        var productionTarget = environment.Contains("prod", StringComparison.Ordinal) ||
            environment.Contains("production", StringComparison.Ordinal) ||
            policyTag.Contains("prod", StringComparison.Ordinal) ||
            policyTag.Contains("production", StringComparison.Ordinal);

        var deploymentAction = action.Contains("deploy", StringComparison.Ordinal) ||
            action.Contains("promote", StringComparison.Ordinal) ||
            action.Contains("rollback", StringComparison.Ordinal);

        return productionTarget && deploymentAction;
    }

    private static PolicyDecision CreateDecision(
        string kind,
        string rationale,
        int riskScore,
        string riskLevel,
        IReadOnlyList<string> riskFactors,
        IReadOnlyList<string> constraints)
    {
        return new PolicyDecision
        {
            Kind = kind,
            PolicyId = PolicyId,
            PolicyName = PolicyName,
            Rationale = rationale,
            RiskScore = riskScore,
            RiskLevel = riskLevel,
            RiskFactors = riskFactors.ToList(),
            DecidedAt = DateTime.UtcNow.ToString("o"),
            Constraints = constraints.ToList()
        };
    }
}

using System.Collections.Generic;

namespace Autofac.Domain.Persistence;

public sealed class PolicyDecision
{
    public string Kind { get; set; } = "allow";

    public string PolicyId { get; set; } = string.Empty;

    public string PolicyName { get; set; } = string.Empty;

    public string Rationale { get; set; } = string.Empty;

    public int RiskScore { get; set; }

    public string RiskLevel { get; set; } = "low";

    public List<string> RiskFactors { get; set; } = new();

    /// <summary>
    /// Confidence (0–100) that the stated purpose is corroborated by the request's
    /// declared context (action, environment, policy tag, evidence). Attached to
    /// every decision by the purpose/risk scorer (#26).
    /// </summary>
    public int PurposeConfidence { get; set; }

    /// <summary>Human-readable explanation of how the purpose confidence was derived.</summary>
    public string PurposeRationale { get; set; } = string.Empty;

    public string DecidedAt { get; set; } = string.Empty;

    public List<string> Constraints { get; set; } = new();
}

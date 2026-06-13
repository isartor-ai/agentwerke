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

    public string DecidedAt { get; set; } = string.Empty;

    public List<string> Constraints { get; set; } = new();
}

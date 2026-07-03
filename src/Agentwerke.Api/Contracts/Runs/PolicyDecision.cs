using System.Collections.Generic;

namespace Agentwerke.Api.Contracts.Runs;

public sealed record PolicyDecision(
    string Kind,
    string PolicyId,
    string PolicyName,
    string Rationale,
    int RiskScore,
    string RiskLevel,
    IReadOnlyList<string> RiskFactors,
    string DecidedAt,
    IReadOnlyList<string> Constraints,
    int PurposeConfidence,
    string PurposeRationale);

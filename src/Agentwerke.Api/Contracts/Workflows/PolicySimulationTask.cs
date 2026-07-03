using System.Collections.Generic;

namespace Agentwerke.Api.Contracts.Workflows;

public sealed record PolicySimulationTask(
    string NodeId,
    string RiskLevel,
    IReadOnlyList<string> RequiredApprovals,
    IReadOnlyList<string> RequiredEvidence);

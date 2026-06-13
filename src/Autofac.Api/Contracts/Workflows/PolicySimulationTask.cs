using System.Collections.Generic;

namespace Autofac.Api.Contracts.Workflows;

public sealed record PolicySimulationTask(
    string NodeId,
    string RiskLevel,
    IReadOnlyList<string> RequiredApprovals,
    IReadOnlyList<string> RequiredEvidence);

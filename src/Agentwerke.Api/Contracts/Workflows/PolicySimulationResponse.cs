using System.Collections.Generic;

namespace Agentwerke.Api.Contracts.Workflows;

public sealed record PolicySimulationResponse(IReadOnlyList<PolicySimulationTask> Tasks);

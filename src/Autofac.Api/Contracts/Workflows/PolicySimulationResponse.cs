using System.Collections.Generic;

namespace Autofac.Api.Contracts.Workflows;

public sealed record PolicySimulationResponse(IReadOnlyList<PolicySimulationTask> Tasks);

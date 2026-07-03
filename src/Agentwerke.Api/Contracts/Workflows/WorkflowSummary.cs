using System.Collections.Generic;

namespace Agentwerke.Api.Contracts.Workflows;

public sealed record WorkflowSummary(
    string Id,
    string Name,
    string Description,
    string Version,
    string Status,
    string Owner,
    string CreatedAt,
    string LastEditedAt,
    string ValidationState,
    IReadOnlyList<string> Tags);

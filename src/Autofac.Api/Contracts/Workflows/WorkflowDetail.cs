using System.Collections.Generic;

namespace Autofac.Api.Contracts.Workflows;

public sealed record WorkflowDetail(
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

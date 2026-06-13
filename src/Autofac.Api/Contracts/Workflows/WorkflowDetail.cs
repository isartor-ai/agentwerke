namespace Autofac.Api.Contracts.Workflows;

public sealed record WorkflowDetail(
    string WorkflowId,
    string Name,
    string Status,
    DateTimeOffset UpdatedAtUtc);

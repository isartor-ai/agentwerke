namespace Autofac.Api.Contracts.Workflows;

public sealed record ValidationWarningResponse(
    string Message,
    string? ElementId,
    string ElementName,
    int? LineNumber,
    int? LinePosition);

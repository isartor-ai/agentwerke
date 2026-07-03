namespace Agentwerke.Api.Contracts.Workflows;

public sealed record ValidationErrorResponse(
    string Message,
    string? ElementId,
    string ElementName,
    int? LineNumber,
    int? LinePosition);

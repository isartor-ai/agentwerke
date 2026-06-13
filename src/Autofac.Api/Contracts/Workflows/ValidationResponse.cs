using System.Collections.Generic;

namespace Autofac.Api.Contracts.Workflows;

public sealed record ValidationResponse(
    bool IsValid,
    string? ProcessId,
    string? ProcessName,
    IReadOnlyList<ValidationErrorResponse> Errors);

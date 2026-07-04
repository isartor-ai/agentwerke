using System.Collections.Generic;

namespace Agentwerke.Api.Contracts.Workflows;

public sealed record ValidationResponse(
    bool IsValid,
    string? ProcessId,
    string? ProcessName,
    IReadOnlyList<ValidationErrorResponse> Errors,
    IReadOnlyList<ValidationWarningResponse> Warnings);

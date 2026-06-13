using System;

namespace Autofac.Api.Contracts.Approvals;

public sealed record ApprovalDecisionResponse(
    string ApprovalId,
    string Status,
    DateTimeOffset DecidedAt,
    string DecidedBy,
    string? Comment);

namespace Agentwerke.Api.Contracts.Approvals;

public sealed record ApprovalDecisionResponse(
    string ApprovalId,
    string Status,
    string DecidedAt,
    string DecidedBy,
    string? Comment);

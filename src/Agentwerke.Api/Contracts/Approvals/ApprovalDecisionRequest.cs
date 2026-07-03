namespace Agentwerke.Api.Contracts.Approvals;

public sealed record ApprovalDecisionRequest(string Decision, string? Comment);

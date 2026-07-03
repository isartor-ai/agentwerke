using System.Collections.Generic;

namespace Agentwerke.Api.Contracts.Runs;

public sealed record RunSummary(
    string Id,
    string WorkflowId,
    string WorkflowName,
    string WorkflowVersion,
    string Status,
    string RiskLevel,
    string? CurrentStep,
    string RequestedBy,
    string StartedAt,
    string? CompletedAt,
    int? DurationMs,
    int PendingApprovals,
    IReadOnlyList<string> Tags,
    IReadOnlyList<RunEvent> Events);

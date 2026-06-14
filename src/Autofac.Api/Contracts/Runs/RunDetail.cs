using Autofac.Api.Contracts.Approvals;
using System.Collections.Generic;

namespace Autofac.Api.Contracts.Runs;

public sealed record RunDetail(
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
    IReadOnlyList<RunEvent> Events,
    IReadOnlyList<RunStep> Steps,
    IReadOnlyList<RunArtifact> Artifacts,
    IReadOnlyList<ApprovalSummary> Approvals);

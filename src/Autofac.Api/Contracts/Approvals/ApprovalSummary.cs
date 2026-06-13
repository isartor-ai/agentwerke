using System;
using System.Collections.Generic;

namespace Autofac.Api.Contracts.Approvals;

public sealed record ApprovalSummary(
    string Id,
    string RunId,
    string WorkflowName,
    string ActionRequested,
    string Requester,
    string AgentName,
    string PolicyRationale,
    int RiskScore,
    string RiskLevel,
    IReadOnlyList<string> RiskFactors,
    IReadOnlyList<string> AffectedSystems,
    string SlaDeadline,
    string CreatedAt,
    string Status,
    string Priority,
    string? DecisionComment = null,
    string? DecidedBy = null,
    string? DecidedAt = null);

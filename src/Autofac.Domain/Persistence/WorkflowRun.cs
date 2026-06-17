using System.Collections.Generic;

namespace Autofac.Domain.Persistence;

public sealed class WorkflowRun
{
    public string Id { get; set; } = string.Empty;

    public string WorkflowId { get; set; } = string.Empty;

    public string WorkflowName { get; set; } = string.Empty;

    public string WorkflowVersion { get; set; } = string.Empty;

    public string Status { get; set; } = "pending";

    public string RiskLevel { get; set; } = "low";

    public string CurrentStep { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;

    public string StartedAt { get; set; } = string.Empty;

    public string? CompletedAt { get; set; }

    public int? DurationMs { get; set; }

    public int PendingApprovals { get; set; }

    public List<string> Tags { get; set; } = new();

    public string? CorrelationId { get; set; }

    public string? CamundaProcessInstanceKey { get; set; }

    public string? CamundaProcessDefinitionKey { get; set; }

    public string? CamundaProcessDefinitionId { get; set; }

    public int? CamundaProcessDefinitionVersion { get; set; }

    public List<WorkflowRunStep> Steps { get; set; } = new();

    public List<WorkflowEvent> Events { get; set; } = new();
}

using Autofac.Domain.AgentRuntime;

namespace Autofac.Domain.Persistence;

public sealed class WorkflowRunStep
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Status { get; set; } = "pending";

    public string? StartedAt { get; set; }

    public string? CompletedAt { get; set; }

    public string? AgentName { get; set; }

    public string? Output { get; set; }

    public string? Error { get; set; }

    public PolicyDecision? PolicyDecision { get; set; }

    public AgentRuntimeSnapshot? RuntimeSnapshot { get; set; }
}

namespace Autofac.Domain.Persistence;

public sealed class PolicyDecision
{
    public Guid Id { get; set; }

    public Guid? WorkflowRunId { get; set; }

    public string PolicyName { get; set; } = string.Empty;

    public string Decision { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string EvidenceJson { get; set; } = "{}";

    public DateTimeOffset EvaluatedAtUtc { get; set; }

    public WorkflowRun? WorkflowRun { get; set; }
}

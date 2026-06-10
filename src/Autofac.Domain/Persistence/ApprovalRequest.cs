namespace Autofac.Domain.Persistence;

public sealed class ApprovalRequest
{
    public Guid Id { get; set; }

    public Guid WorkflowRunId { get; set; }

    public string ApprovalType { get; set; } = string.Empty;

    public string Status { get; set; } = "pending";

    public string RequestedBy { get; set; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }

    public WorkflowRun WorkflowRun { get; set; } = null!;
}

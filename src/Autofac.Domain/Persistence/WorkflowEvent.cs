namespace Autofac.Domain.Persistence;

public sealed class WorkflowEvent
{
    public Guid Id { get; set; }

    public Guid WorkflowRunId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; }

    public WorkflowRun WorkflowRun { get; set; } = null!;
}

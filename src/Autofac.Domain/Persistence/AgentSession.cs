namespace Autofac.Domain.Persistence;

public sealed class AgentSession
{
    public Guid Id { get; set; }

    public Guid WorkflowRunId { get; set; }

    public string AgentName { get; set; } = string.Empty;

    public string Status { get; set; } = "started";

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? EndedAtUtc { get; set; }

    public WorkflowRun WorkflowRun { get; set; } = null!;
}

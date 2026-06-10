namespace Autofac.Domain.Persistence;

public sealed class WorkflowDefinition
{
    public Guid Id { get; set; }

    public string WorkflowKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Status { get; set; } = "draft";

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<WorkflowRun> Runs { get; set; } = new List<WorkflowRun>();
}

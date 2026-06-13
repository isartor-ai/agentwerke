namespace Autofac.Domain.Persistence;

public sealed class WorkflowEvent
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
}

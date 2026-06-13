namespace Autofac.Domain.Persistence;

public sealed class WorkflowDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Status { get; set; } = "draft";

    public string Owner { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;

    public string LastEditedAt { get; set; } = string.Empty;

    public string ValidationState { get; set; } = "pending";

    public List<string> Tags { get; set; } = new();

    public string BpmnXml { get; set; } = string.Empty;
}

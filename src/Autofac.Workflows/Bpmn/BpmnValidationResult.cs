namespace Autofac.Workflows.Bpmn;

public sealed class BpmnValidationResult
{
    public BpmnValidationResult(
        BpmnWorkflowDefinition? definition,
        IReadOnlyList<BpmnValidationError> errors,
        IReadOnlyList<BpmnValidationWarning>? warnings = null)
    {
        Definition = definition;
        Errors = errors;
        Warnings = warnings ?? Array.Empty<BpmnValidationWarning>();
    }

    public BpmnWorkflowDefinition? Definition { get; }

    public IReadOnlyList<BpmnValidationError> Errors { get; }

    public IReadOnlyList<BpmnValidationWarning> Warnings { get; }

    public bool IsValid => Errors.Count == 0;
}

public sealed record BpmnValidationError(
    string Message,
    string? ElementId,
    string ElementName,
    int? LineNumber,
    int? LinePosition);

public sealed record BpmnValidationWarning(
    string Message,
    string? ElementId,
    string ElementName,
    int? LineNumber,
    int? LinePosition);

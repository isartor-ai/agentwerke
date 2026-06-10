namespace Autofac.Workflows.Bpmn;

public sealed class BpmnValidationResult
{
    public BpmnValidationResult(BpmnWorkflowDefinition? definition, IReadOnlyList<BpmnValidationError> errors)
    {
        Definition = definition;
        Errors = errors;
    }

    public BpmnWorkflowDefinition? Definition { get; }

    public IReadOnlyList<BpmnValidationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;
}

public sealed record BpmnValidationError(
    string Message,
    string? ElementId,
    string ElementName,
    int? LineNumber,
    int? LinePosition);

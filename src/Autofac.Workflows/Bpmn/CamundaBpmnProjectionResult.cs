namespace Autofac.Workflows.Bpmn;

public sealed class CamundaBpmnProjectionResult
{
    public CamundaBpmnProjectionResult(
        BpmnWorkflowDefinition? definition,
        string? projectedBpmnXml,
        IReadOnlyList<BpmnValidationError> errors,
        IReadOnlyList<BpmnValidationWarning> warnings,
        IReadOnlyList<CamundaTaskBinding> bindings)
    {
        Definition = definition;
        ProjectedBpmnXml = projectedBpmnXml;
        Errors = errors;
        Warnings = warnings;
        Bindings = bindings;
    }

    public BpmnWorkflowDefinition? Definition { get; }

    public string? ProjectedBpmnXml { get; }

    public IReadOnlyList<BpmnValidationError> Errors { get; }

    public IReadOnlyList<BpmnValidationWarning> Warnings { get; }

    public IReadOnlyList<CamundaTaskBinding> Bindings { get; }

    public bool IsValid => Errors.Count == 0 && !string.IsNullOrWhiteSpace(ProjectedBpmnXml);
}

public sealed record CamundaTaskBinding(
    string ElementId,
    string ElementName,
    string? JobType,
    IReadOnlyDictionary<string, string> TaskHeaders,
    AutofacTaskMetadata? AgentMetadata = null,
    AutofacApprovalMetadata? ApprovalMetadata = null);

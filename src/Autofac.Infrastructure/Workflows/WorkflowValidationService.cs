using Autofac.Application.Workflows;
using Autofac.Workflows.Bpmn;

namespace Autofac.Infrastructure.Workflows;

public sealed class WorkflowValidationService : IWorkflowValidationService
{
    private readonly ICamundaBpmnProjector _projector;

    public WorkflowValidationService(ICamundaBpmnProjector projector)
    {
        _projector = projector;
    }

    public WorkflowValidationResult Validate(string bpmnXml)
    {
        var validation = _projector.Project(bpmnXml);
        return new WorkflowValidationResult(
            validation.IsValid,
            validation.Definition?.ProcessId,
            validation.Definition?.ProcessName,
            validation.Errors.Select(error => new WorkflowValidationError(
                error.Message,
                error.ElementId,
                error.ElementName ?? "document",
                error.LineNumber,
                error.LinePosition)).ToArray(),
            validation.Warnings.Select(warning => new WorkflowValidationWarning(
                warning.Message,
                warning.ElementId,
                warning.ElementName ?? "document",
                warning.LineNumber,
                warning.LinePosition)).ToArray());
    }
}

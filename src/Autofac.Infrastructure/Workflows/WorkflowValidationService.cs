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
        return MapPreparation(_projector.Project(bpmnXml)).Validation;
    }

    public WorkflowPublishPreparation PrepareForPublish(string bpmnXml)
    {
        return MapPreparation(_projector.Project(bpmnXml));
    }

    private static WorkflowPublishPreparation MapPreparation(CamundaBpmnProjectionResult projection)
    {
        var validation = new WorkflowValidationResult(
            projection.IsValid,
            projection.Definition?.ProcessId,
            projection.Definition?.ProcessName,
            projection.Errors.Select(error => new WorkflowValidationError(
                error.Message,
                error.ElementId,
                error.ElementName ?? "document",
                error.LineNumber,
                error.LinePosition)).ToArray(),
            projection.Warnings.Select(warning => new WorkflowValidationWarning(
                warning.Message,
                warning.ElementId,
                warning.ElementName ?? "document",
                warning.LineNumber,
                warning.LinePosition)).ToArray());

        return new WorkflowPublishPreparation(validation, projection.ProjectedBpmnXml);
    }
}

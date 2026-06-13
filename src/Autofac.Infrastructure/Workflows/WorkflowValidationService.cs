using Autofac.Application.Workflows;
using Autofac.Workflows.Bpmn;

namespace Autofac.Infrastructure.Workflows;

public sealed class WorkflowValidationService : IWorkflowValidationService
{
    private readonly IBpmnWorkflowValidator _validator;

    public WorkflowValidationService(IBpmnWorkflowValidator validator)
    {
        _validator = validator;
    }

    public WorkflowValidationResult Validate(string bpmnXml)
    {
        var validation = _validator.Validate(bpmnXml);
        return new WorkflowValidationResult(
            validation.IsValid,
            validation.Definition?.ProcessId,
            validation.Definition?.ProcessName,
            validation.Errors.Select(error => new WorkflowValidationError(
                error.Message,
                error.ElementId,
                error.ElementName ?? "document",
                error.LineNumber,
                error.LinePosition)).ToArray());
    }
}

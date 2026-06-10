namespace Autofac.Workflows.Bpmn;

public interface IBpmnWorkflowValidator
{
    BpmnValidationResult Validate(string bpmnXml);
}

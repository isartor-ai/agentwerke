namespace Agentwerke.Workflows.Bpmn;

public interface IBpmnWorkflowValidator
{
    BpmnValidationResult Validate(string bpmnXml);
}

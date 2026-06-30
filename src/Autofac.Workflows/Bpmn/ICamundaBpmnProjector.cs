namespace Autofac.Workflows.Bpmn;

public interface ICamundaBpmnProjector
{
    CamundaBpmnProjectionResult Project(string bpmnXml);
}

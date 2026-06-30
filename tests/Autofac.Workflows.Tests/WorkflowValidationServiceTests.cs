using Autofac.Infrastructure.Workflows;
using Autofac.Workflows.Bpmn;

namespace Autofac.Workflows.Tests;

public sealed class WorkflowValidationServiceTests
{
    [Fact]
    public void Validate_WhenProjectionFails_ReturnsInvalidWorkflowValidationResult()
    {
        var service = new WorkflowValidationService(
            new CamundaBpmnProjector(new BpmnWorkflowValidator()));

        var validation = service.Validate(
            """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="UnsupportedProjectionFlow" name="Unsupported Projection Flow">
                <bpmn:startEvent id="Start">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="implementation-agent"
                      action="code.generate"
                      purposeType="implementation"
                      policyTag="repo-change" />
                  </bpmn:extensionElements>
                </bpmn:startEvent>
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error =>
            error.ElementId == "Start" &&
            error.Message.Contains("Autofac metadata is only supported", StringComparison.Ordinal));
    }
}

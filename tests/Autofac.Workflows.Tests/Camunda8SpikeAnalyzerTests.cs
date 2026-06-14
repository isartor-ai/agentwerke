using Autofac.Workflows.Camunda;

namespace Autofac.Workflows.Tests;

public sealed class Camunda8SpikeAnalyzerTests
{
    [Fact]
    public void Analyze_WhenMvpFlowProvided_MapsServiceUserTimerAndMessageStartConstructs()
    {
        var analyzer = new Camunda8SpikeAnalyzer();

        var report = analyzer.Analyze(
            """
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <bpmn:process id="Process_1" name="Camunda Spike">
                <bpmn:startEvent id="ApiStart" />
                <bpmn:startEvent id="MessageStart">
                  <bpmn:messageEventDefinition />
                </bpmn:startEvent>
                <bpmn:startEvent id="TimerStart">
                  <bpmn:timerEventDefinition />
                </bpmn:startEvent>
                <bpmn:serviceTask id="ServiceTask_1" name="Generate PR" />
                <bpmn:userTask id="UserTask_1" name="Approve PR" />
                <bpmn:intermediateCatchEvent id="RetryDelay">
                  <bpmn:timerEventDefinition />
                </bpmn:intermediateCatchEvent>
              </bpmn:process>
            </bpmn:definitions>
            """);

        Assert.Equal("camunda8-spike", report.EngineId);
        Assert.Contains(report.ElementMappings, static item =>
            item.ElementId == "ServiceTask_1" &&
            item.CamundaConstruct == "service task" &&
            item.ExecutionPattern == "job worker");
        Assert.Contains(report.ElementMappings, static item =>
            item.ElementId == "UserTask_1" &&
            item.CamundaConstruct == "user task");
        Assert.Contains(report.ElementMappings, static item =>
            item.ElementId == "RetryDelay" &&
            item.CamundaConstruct == "intermediate timer catch event");
        Assert.Contains(report.ElementMappings, static item =>
            item.ElementId == "MessageStart" &&
            item.CamundaConstruct == "message start event");
        Assert.Contains(report.TriggerMappings, static item =>
            item.AutofacTrigger == "webhook" &&
            item.CamundaConstruct == "HTTP webhook inbound connector");
    }
}

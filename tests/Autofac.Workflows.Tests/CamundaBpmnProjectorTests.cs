using System.Xml.Linq;
using Autofac.Workflows.Bpmn;

namespace Autofac.Workflows.Tests;

public sealed class CamundaBpmnProjectorTests
{
    private static readonly XNamespace BpmnNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace ZeebeNamespace = "http://camunda.org/schema/zeebe/1.0";

    private readonly CamundaBpmnProjector _projector = new(new BpmnWorkflowValidator());

    [Fact]
    public void Project_WhenAgentTaskIsPresent_ConvertsItToCamundaServiceTaskWithHeaders()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="ProjectionFlow" name="Projection Flow">
                <bpmn:startEvent id="Start" />
                <bpmn:scriptTask id="ImplementTask" name="Implement">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="implementation-agent"
                      action="code.generate"
                      environment="repo"
                      purposeType="implementation"
                      policyTag="repo-change"
                      requiresEvidence="diff,test-results"
                      maxRetries="5"
                      retryBackoffSeconds="30"
                      timeoutSeconds="600" />
                  </bpmn:extensionElements>
                </bpmn:scriptTask>
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _projector.Project(xml);

        Assert.True(result.IsValid);
        Assert.NotNull(result.ProjectedBpmnXml);
        Assert.Single(result.Bindings);

        var document = XDocument.Parse(result.ProjectedBpmnXml!);
        var task = Assert.Single(document.Descendants(BpmnNamespace + "serviceTask"));
        Assert.Equal("ImplementTask", task.Attribute("id")?.Value);
        Assert.Null(document.Descendants(BpmnNamespace + "scriptTask").FirstOrDefault());

        var extensionElements = Assert.Single(task.Elements(BpmnNamespace + "extensionElements"));
        var taskDefinition = Assert.Single(extensionElements.Elements(ZeebeNamespace + "taskDefinition"));
        Assert.Equal("autofac.agent", taskDefinition.Attribute("type")?.Value);
        Assert.Equal("5", taskDefinition.Attribute("retries")?.Value);

        var taskHeaders = Assert.Single(extensionElements.Elements(ZeebeNamespace + "taskHeaders"));
        var headers = taskHeaders.Elements(ZeebeNamespace + "header")
            .ToDictionary(
                header => header.Attribute("key")?.Value ?? string.Empty,
                header => header.Attribute("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        Assert.Equal("ImplementTask", headers["autofac.elementId"]);
        Assert.Equal("implementation-agent", headers["autofac.agent"]);
        Assert.Equal("code.generate", headers["autofac.action"]);
        Assert.Equal("repo", headers["autofac.environment"]);
        Assert.Equal("implementation", headers["autofac.purposeType"]);
        Assert.Equal("repo-change", headers["autofac.policyTag"]);
        Assert.Equal("diff,test-results", headers["autofac.requiresEvidence"]);
        Assert.Equal("30", headers["autofac.retryBackoffSeconds"]);
        Assert.Equal("600", headers["autofac.timeoutSeconds"]);

        Assert.DoesNotContain(
            extensionElements.Elements(),
            element => element.Name.NamespaceName.Contains("autofac", StringComparison.Ordinal));

        var binding = Assert.Single(result.Bindings);
        Assert.Equal("ImplementTask", binding.ElementId);
        Assert.Equal("autofac.agent", binding.JobType);
        Assert.Equal("implementation-agent", binding.AgentMetadata?.Agent);
        Assert.Equal("repo-change", binding.TaskHeaders["autofac.policyTag"]);
    }

    [Fact]
    public void Project_WhenApprovalTaskIsPresent_PreservesUserTaskAndMarksItAsCamundaUserTask()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="ApprovalFlow" name="Approval Flow">
                <bpmn:userTask id="ApprovalTask" name="Approve change">
                  <bpmn:extensionElements>
                    <autofac:approvalTask
                      purposeType="deployment_approval"
                      policyTag="release-gate" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _projector.Project(xml);

        Assert.True(result.IsValid);
        Assert.NotNull(result.ProjectedBpmnXml);

        var document = XDocument.Parse(result.ProjectedBpmnXml!);
        var task = Assert.Single(document.Descendants(BpmnNamespace + "userTask"));
        var extensionElements = Assert.Single(task.Elements(BpmnNamespace + "extensionElements"));
        Assert.Single(extensionElements.Elements(ZeebeNamespace + "userTask"));
        Assert.DoesNotContain(
            extensionElements.Elements(),
            element => element.Name.LocalName == "approvalTask");

        var binding = Assert.Single(result.Bindings);
        Assert.Equal("ApprovalTask", binding.ElementId);
        Assert.Null(binding.JobType);
        Assert.Equal("deployment_approval", binding.ApprovalMetadata?.PurposeType);
        Assert.Equal("release-gate", binding.ApprovalMetadata?.PolicyTag);
    }

    [Fact]
    public void Project_WhenAgentTaskExplicitlyDisablesRetries_PreservesZeroRetries()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="ZeroRetryFlow" name="Zero Retry Flow">
                <bpmn:serviceTask id="ImplementTask" name="Implement">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="implementation-agent"
                      action="code.generate"
                      purposeType="implementation"
                      policyTag="repo-change"
                      maxRetries="0" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _projector.Project(xml);

        Assert.True(result.IsValid);
        var document = XDocument.Parse(result.ProjectedBpmnXml!);
        var task = Assert.Single(document.Descendants(BpmnNamespace + "serviceTask"));
        var extensionElements = Assert.Single(task.Elements(BpmnNamespace + "extensionElements"));
        var taskDefinition = Assert.Single(extensionElements.Elements(ZeebeNamespace + "taskDefinition"));

        Assert.Equal("0", taskDefinition.Attribute("retries")?.Value);
    }

    [Fact]
    public void Project_WhenAgentTaskMetadataIsMissing_ReturnsValidationErrors()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="BrokenFlow">
                <bpmn:serviceTask id="Task1">
                  <bpmn:extensionElements>
                    <autofac:agentTask action="code.generate" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _projector.Project(xml);

        Assert.False(result.IsValid);
        Assert.Null(result.ProjectedBpmnXml);
        Assert.Contains(result.Errors, error =>
            error.ElementId == "Task1" &&
            error.Message.Contains("missing required attributes", StringComparison.Ordinal));
    }

    [Fact]
    public void Project_WhenAutofacMetadataIsAttachedToUnsupportedElement_ReturnsProjectionError()
    {
        var xml = """
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
            """;

        var result = _projector.Project(xml);

        Assert.False(result.IsValid);
        Assert.Null(result.ProjectedBpmnXml);
        Assert.Contains(result.Errors, error =>
            error.ElementId == "Start" &&
            error.Message.Contains("Autofac metadata is only supported on serviceTask, scriptTask, and userTask elements", StringComparison.Ordinal));
    }
}

using Autofac.Workflows.Bpmn;

namespace Autofac.Workflows.Tests;

public sealed class BpmnWorkflowValidatorTests
{
    private readonly BpmnWorkflowValidator _validator = new();

    [Fact]
    public void Validate_WhenWorkflowIsValid_ReturnsDefinitionWithoutErrors()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.de/bpmn/extensions/v1">
              <bpmn:process id="DeployWorkflow" name="Deploy Workflow">
                <bpmn:startEvent id="Start" />
                <bpmn:serviceTask id="DeployTask" name="Deploy">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="DeploymentAgent"
                      action="cloud.deploy_artifact"
                      environment="production"
                      purposeType="production_deployment"
                      policyTag="production_deployment_gateway"
                      requiresEvidence="ci_passed,sast_passed,human_approval" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:userTask id="ApprovalTask" name="Human Approval">
                  <bpmn:extensionElements>
                    <autofac:approvalTask
                      purposeType="production_deployment"
                      policyTag="human_approval_required" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
                <bpmn:intermediateCatchEvent id="RetryDelay">
                  <bpmn:timerEventDefinition />
                </bpmn:intermediateCatchEvent>
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.NotNull(result.Definition);
        Assert.Equal("DeployWorkflow", result.Definition!.ProcessId);

        var deployNode = Assert.Single(result.Definition.Nodes, node => node.Id == "DeployTask");
        Assert.NotNull(deployNode.Metadata);
        Assert.Equal("DeploymentAgent", deployNode.Metadata!.Agent);
        Assert.Equal("cloud.deploy_artifact", deployNode.Metadata.Action);
        Assert.Equal("production_deployment", deployNode.Metadata.PurposeType);
        Assert.Equal("production_deployment_gateway", deployNode.Metadata.PolicyTag);
        Assert.Equal(3, deployNode.Metadata.RequiresEvidence.Count);

        var approvalNode = Assert.Single(result.Definition.Nodes, node => node.Id == "ApprovalTask");
        Assert.NotNull(approvalNode.ApprovalMetadata);
        Assert.Equal("production_deployment", approvalNode.ApprovalMetadata!.PurposeType);
        Assert.Equal("human_approval_required", approvalNode.ApprovalMetadata.PolicyTag);
    }

    [Fact]
    public void Validate_WhenUnsupportedElementIsPresent_ReturnsActionableError()
    {
        var xml = """
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <bpmn:process id="UnsupportedFlow">
                <bpmn:startEvent id="Start" />
                <bpmn:manualTask id="Manual1" name="Manual intervention" />
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Manual1", error.ElementId);
        Assert.Contains("Unsupported BPMN element 'manualTask'", error.Message);
        Assert.Equal("manualTask", error.ElementName);
    }

    [Fact]
    public void Validate_WhenServiceTaskMetadataIsMissing_ReturnsRequiredAttributeErrors()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.de/bpmn/extensions/v1">
              <bpmn:process id="MetadataFlow">
                <bpmn:serviceTask id="Task1">
                  <bpmn:extensionElements>
                    <autofac:agentTask action="cloud.deploy_artifact" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Task1", error.ElementId);
        Assert.Contains("missing required attributes", error.Message);
        Assert.Contains("agent", error.Message);
        Assert.Contains("purposeType", error.Message);
        Assert.Contains("policyTag", error.Message);
    }

    [Fact]
    public void Validate_WhenTimerAndBoundaryDefinitionsAreMissing_ReturnsSpecificErrors()
    {
        var xml = """
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <bpmn:process id="EventsFlow">
                <bpmn:intermediateCatchEvent id="WaitForRetry" />
                <bpmn:boundaryEvent id="BoundaryTimeout" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, error =>
            error.ElementId == "WaitForRetry" &&
            error.Message.Contains("timerEventDefinition", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error =>
            error.ElementId == "BoundaryTimeout" &&
            error.Message.Contains("Boundary event must define", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenAutofacMetadataHasNonBlockingIssues_ReturnsActionableWarnings()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.de/bpmn/extensions/v1">
              <bpmn:process id="WarnFlow">
                <bpmn:serviceTask id="DeployTask">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="DeploymentAgent"
                      action="cloud.deploy_artifact"
                      purposeType="production_deployment"
                      policyTag="production_deployment_gateway"
                      requiresEvidence="ci_passed,ci_passed"
                      maxRetries="2"
                      simulateTimeout="true" />
                    <autofac:approvalTask
                      purposeType="production_deployment"
                      policyTag="human_approval_required" />
                    <autofac:notify channel="slack" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:userTask id="ApprovalTask">
                  <bpmn:extensionElements>
                    <autofac:approvalTask
                      purposeType="production_deployment"
                      policyTag="human_approval_required" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, warning =>
            warning.ElementName == "process" &&
            warning.Message.Contains("missing a human-readable 'name' attribute", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning =>
            warning.ElementId == "DeployTask" &&
            warning.Message.Contains("autofac:approvalTask metadata", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning =>
            warning.ElementId == "DeployTask" &&
            warning.Message.Contains("duplicate entries", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning =>
            warning.ElementId == "DeployTask" &&
            warning.Message.Contains("simulateTimeout='true' without timeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning =>
            warning.ElementId == "DeployTask" &&
            warning.Message.Contains("Unexpected Autofac extension element 'notify'", StringComparison.Ordinal));
    }
}

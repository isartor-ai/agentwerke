using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Workflows.Bpmn;

namespace Agentwerke.Workflows.Tests;

public sealed class BpmnWorkflowValidatorTests
{
    private readonly BpmnWorkflowValidator _validator = new();

    [Fact]
    public void Validate_WhenWorkflowIsValid_ReturnsDefinitionWithoutErrors()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="DeployWorkflow" name="Deploy Workflow">
                <bpmn:startEvent id="Start" />
                <bpmn:serviceTask id="DeployTask" name="Deploy">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
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
                    <agentwerke:approvalTask
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
    public void Validate_WhenMessageCatchEventHasExternalMetadata_ParsesDefinition()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="ExternalWorkflow" name="External Workflow">
                <bpmn:startEvent id="Start" />
                <bpmn:intermediateCatchEvent id="WaitForMerge" name="Wait For Merge">
                  <bpmn:extensionElements>
                    <agentwerke:externalEvent
                      messageName="github.pull_request.merged"
                      correlationKeyTemplate="{{run_context.branch_name}}" />
                  </bpmn:extensionElements>
                  <bpmn:messageEventDefinition />
                </bpmn:intermediateCatchEvent>
                <bpmn:receiveTask id="ReceiveWebhook" name="Receive Webhook">
                  <bpmn:extensionElements>
                    <agentwerke:externalEvent
                      messageName="github.webhook.received"
                      correlationKeyTemplate="{{run_context.workflow_ref}}" />
                  </bpmn:extensionElements>
                </bpmn:receiveTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Definition);

        var waitForMerge = Assert.Single(result.Definition!.Nodes, node => node.Id == "WaitForMerge");
        Assert.Equal("github.pull_request.merged", waitForMerge.ExternalEventMetadata?.MessageName);
        Assert.Equal("{{run_context.branch_name}}", waitForMerge.ExternalEventMetadata?.CorrelationKeyTemplate);

        var receiveWebhook = Assert.Single(result.Definition.Nodes, node => node.Id == "ReceiveWebhook");
        Assert.Equal("receiveTask", receiveWebhook.ElementName);
        Assert.Equal("github.webhook.received", receiveWebhook.ExternalEventMetadata?.MessageName);
        Assert.Equal("{{run_context.workflow_ref}}", receiveWebhook.ExternalEventMetadata?.CorrelationKeyTemplate);
    }

    [Fact]
    public void Validate_WhenMessageCatchEventMissesCorrelationTemplate_ReturnsActionableError()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="ExternalWorkflow" name="External Workflow">
                <bpmn:startEvent id="Start" />
                <bpmn:intermediateCatchEvent id="WaitForMerge">
                  <bpmn:extensionElements>
                    <agentwerke:externalEvent
                      messageName="github.pull_request.merged" />
                  </bpmn:extensionElements>
                  <bpmn:messageEventDefinition />
                </bpmn:intermediateCatchEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("WaitForMerge", error.ElementId);
        Assert.Contains("correlationKeyTemplate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenSandboxProfileAttributeIsPresent_ParsesIntoMetadata()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="DeployWorkflow" name="Deploy Workflow">
                <bpmn:serviceTask id="DeployTask" name="Deploy">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
                      agent="deploy-agent"
                      action="deploy"
                      environment="production"
                      purposeType="production_deployment"
                      policyTag="production_deployment_gateway"
                      sandboxProfile="deployment" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.Empty(result.Errors);
        var deployNode = Assert.Single(result.Definition!.Nodes, node => node.Id == "DeployTask");
        Assert.Equal("deployment", deployNode.Metadata!.SandboxProfile);
    }

    [Fact]
    public void Validate_WhenExecutionModeAttributeIsPresent_ParsesIntoMetadata()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="DeployWorkflow" name="Deploy Workflow">
                <bpmn:serviceTask id="DeployTask" name="Deploy">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
                      agent="spec-writer"
                      action="spec.generate"
                      environment="staging"
                      purposeType="specification"
                      policyTag="doc-generation"
                      executionMode="agent_sandboxed" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.Empty(result.Errors);
        var deployNode = Assert.Single(result.Definition!.Nodes, node => node.Id == "DeployTask");
        Assert.Equal(AgentExecutionModes.AgentSandboxed, deployNode.Metadata!.ExecutionMode);
    }

    [Fact]
    public void Validate_WhenPromptDeclaresStrictVariables_ParsesPromptContract()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="ReviewWorkflow" name="Review Workflow">
                <bpmn:serviceTask id="ReviewTask" name="Review">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
                      agent="reviewer"
                      action="review.pr"
                      environment="sandbox"
                      purposeType="code_review"
                      policyTag="demo-review"
                      strictVariables="true">
                      <agentwerke:prompt><![CDATA[
                        Review {{output.ImplementIssue}}
                      ]]></agentwerke:prompt>
                    </agentwerke:agentTask>
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.Empty(result.Errors);
        var reviewNode = Assert.Single(result.Definition!.Nodes, node => node.Id == "ReviewTask");
        Assert.True(reviewNode.Metadata!.RuntimeContract!.Prompt!.StrictVariables);
    }

    [Fact]
    public void Validate_WhenSandboxProfileAttributeIsAbsent_MetadataSandboxProfileIsNull()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="DeployWorkflow" name="Deploy Workflow">
                <bpmn:serviceTask id="DeployTask" name="Deploy">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
                      agent="deploy-agent"
                      action="deploy"
                      environment="production"
                      purposeType="production_deployment"
                      policyTag="production_deployment_gateway" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        var deployNode = Assert.Single(result.Definition!.Nodes, node => node.Id == "DeployTask");
        Assert.Null(deployNode.Metadata!.SandboxProfile);
    }

    [Fact]
    public void Validate_WhenRuntimePermissionAttributesArePresent_ParsesRuntimeContract()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="SandboxWorkflow" name="Sandbox Workflow">
                <bpmn:serviceTask id="SandboxTask" name="Run sandbox">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
                      agent="sandbox-e2e-agent"
                      action="run-open-sandbox"
                      environment="ci"
                      purposeType="verification"
                      policyTag="opensandbox-e2e"
                      sandboxProfile="offline"
                      permissionLevel="read-write"
                      allowedTools="sandbox.execute, sandbox.execute"
                      deniedTools="web_search" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.Empty(result.Errors);
        var sandboxNode = Assert.Single(result.Definition!.Nodes, node => node.Id == "SandboxTask");
        var contract = sandboxNode.Metadata!.RuntimeContract;
        Assert.NotNull(contract);
        Assert.Equal("read-write", contract!.Permissions.Level);
        Assert.Equal(["sandbox.execute"], contract.Permissions.AllowedTools);
        Assert.Equal(["web_search"], contract.Permissions.DeniedTools);
    }

    [Fact]
    public void Validate_WhenPromptAttributeIsPresent_ParsesInlinePrompt()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="W" name="W">
                <bpmn:serviceTask id="Analyze" name="Analyze">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask agent="analyst" action="analyze"
                      purposeType="analysis" policyTag="standard"
                      prompt="Summarize {{input.title}} in one sentence." />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.Empty(result.Errors);
        var node = Assert.Single(result.Definition!.Nodes, n => n.Id == "Analyze");
        var contract = node.Metadata!.RuntimeContract;
        Assert.NotNull(contract);
        Assert.Equal("Summarize {{input.title}} in one sentence.", contract!.Prompt!.Inline);
        Assert.Null(contract.Prompt.File);
        // Prompt-only contract still gets the default permission level.
        Assert.Equal("read-only", contract.Permissions.Level);
    }

    [Fact]
    public void Validate_WhenMetadataChildElementsArePresent_ParsesRuntimeMetadata()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="W" name="W">
                <bpmn:serviceTask id="Comment" name="Comment">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask agent="github-agent" action="github.comment_issue"
                      purposeType="documentation" policyTag="issue-comment">
                      <agentwerke:metadata key="tool.input.issue_number" value="{{input.issue_number}}" />
                      <agentwerke:metadata key="tool.input.body">{{output.Requirements}}</agentwerke:metadata>
                    </agentwerke:agentTask>
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.Empty(result.Errors);
        var node = Assert.Single(result.Definition!.Nodes, n => n.Id == "Comment");
        var contract = node.Metadata!.RuntimeContract;
        Assert.NotNull(contract);
        Assert.Equal("{{input.issue_number}}", contract!.Metadata["tool.input.issue_number"]);
        Assert.Equal("{{output.Requirements}}", contract.Metadata["tool.input.body"]);
    }

    [Fact]
    public void Validate_WhenPromptChildElementIsPresent_ParsesAndTrimsMultilinePrompt()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="W" name="W">
                <bpmn:serviceTask id="Impl" name="Impl">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask agent="impl" action="implement"
                      purposeType="implementation" policyTag="repo-change"
                      permissionLevel="read-write">
                      <agentwerke:prompt>
                        Implement the change described in {{input.body}}.
                        Keep it minimal.
                      </agentwerke:prompt>
                    </agentwerke:agentTask>
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.Empty(result.Errors);
        var node = Assert.Single(result.Definition!.Nodes, n => n.Id == "Impl");
        var contract = node.Metadata!.RuntimeContract;
        Assert.NotNull(contract);
        Assert.StartsWith("Implement the change described in {{input.body}}.", contract!.Prompt!.Inline);
        Assert.Contains("Keep it minimal.", contract.Prompt.Inline);
        Assert.Equal("read-write", contract.Permissions.Level);
    }

    [Fact]
    public void Validate_WhenNoPromptOrPermissions_RuntimeContractIsNull()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="W" name="W">
                <bpmn:serviceTask id="Plain" name="Plain">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask agent="a" action="act"
                      purposeType="p" policyTag="t" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.Empty(result.Errors);
        var node = Assert.Single(result.Definition!.Nodes, n => n.Id == "Plain");
        Assert.Null(node.Metadata!.RuntimeContract);
    }

    [Fact]
    public void Validate_WhenRuntimePermissionLevelIsUnknown_ReturnsActionableError()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="SandboxWorkflow" name="Sandbox Workflow">
                <bpmn:serviceTask id="SandboxTask" name="Run sandbox">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
                      agent="sandbox-e2e-agent"
                      action="run-open-sandbox"
                      environment="ci"
                      purposeType="verification"
                      policyTag="opensandbox-e2e"
                      permissionLevel="superuser" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        var error = Assert.Single(result.Errors);
        Assert.Equal("SandboxTask", error.ElementId);
        Assert.Contains("permissionLevel must be one of", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenExecutionModeIsUnknown_ReturnsActionableError()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="SandboxWorkflow" name="Sandbox Workflow">
                <bpmn:serviceTask id="SandboxTask" name="Run sandbox">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
                      agent="sandbox-e2e-agent"
                      action="run-open-sandbox"
                      environment="ci"
                      purposeType="verification"
                      policyTag="opensandbox-e2e"
                      executionMode="super-sandbox" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        var error = Assert.Single(result.Errors);
        Assert.Equal("SandboxTask", error.ElementId);
        Assert.Contains("executionMode must be one of", error.Message, StringComparison.Ordinal);
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
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="MetadataFlow">
                <bpmn:serviceTask id="Task1">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask action="cloud.deploy_artifact" />
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
    public void Validate_WhenAgentwerkeMetadataHasNonBlockingIssues_ReturnsActionableWarnings()
    {
        var xml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="WarnFlow">
                <bpmn:serviceTask id="DeployTask">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
                      agent="DeploymentAgent"
                      action="cloud.deploy_artifact"
                      purposeType="production_deployment"
                      policyTag="production_deployment_gateway"
                      requiresEvidence="ci_passed,ci_passed"
                      maxRetries="2"
                      simulateTimeout="true" />
                    <agentwerke:approvalTask
                      purposeType="production_deployment"
                      policyTag="human_approval_required" />
                    <agentwerke:notify channel="slack" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:userTask id="ApprovalTask">
                  <bpmn:extensionElements>
                    <agentwerke:approvalTask
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
            warning.Message.Contains("agentwerke:approvalTask metadata", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning =>
            warning.ElementId == "DeployTask" &&
            warning.Message.Contains("duplicate entries", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning =>
            warning.ElementId == "DeployTask" &&
            warning.Message.Contains("simulateTimeout='true' without timeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning =>
            warning.ElementId == "DeployTask" &&
            warning.Message.Contains("Unexpected Agentwerke extension element 'notify'", StringComparison.Ordinal));
    }
}

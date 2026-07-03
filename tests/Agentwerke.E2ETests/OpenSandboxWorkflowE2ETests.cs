using System.Text.Json.Nodes;

namespace Agentwerke.E2ETests;

public sealed class OpenSandboxWorkflowE2ETests : E2ETestBase
{
    [OpenSandboxFact]
    public async Task UploadAgentThenRunWorkflow_ExecutesServiceTaskThroughOpenSandbox()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(120));

        var suffix = Guid.NewGuid().ToString("N");
        var agentId = $"opensandbox-e2e-{suffix}";
        var action = "run-open-sandbox";

        var uploadedAgent = await Api.UploadAgentAsync(
            $"{agentId}.md",
            BuildAgentMarkdown(agentId, action));
        Assert.Equal(agentId, uploadedAgent["agentId"]!.GetValue<string>());

        var agent = await Api.GetAgentAsync(agentId);
        Assert.Equal(agentId, agent["agentId"]!.GetValue<string>());
        Assert.Contains(
            agent["sandboxProfiles"]!.AsArray(),
            item => string.Equals(item?.GetValue<string>(), "offline", StringComparison.OrdinalIgnoreCase));

        var bpmn = BuildOpenSandboxWorkflow(agentId, action);
        var workflowId = await Api.ImportWorkflowAsync($"{agentId}.bpmn", bpmn);
        await Api.PublishWorkflowAsync(workflowId, bpmn);

        var (runId, _) = await Api.StartRunAsync(workflowId);
        var run = await Api.PollRunUntilAsync(
            runId,
            IsTerminal,
            TimeSpan.FromSeconds(180));

        var status = run["status"]!.GetValue<string>();
        Assert.True(
            string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase),
            $"Expected completed run but got status '{status}'. Run: {run.ToJsonString()}");

        var step = Assert.Single(run["steps"]!.AsArray().OfType<JsonObject>());
        Assert.True(
            string.Equals(step["status"]!.GetValue<string>(), "completed", StringComparison.OrdinalIgnoreCase),
            $"Expected completed step but got '{step["status"]!.GetValue<string>()}'. Run: {run.ToJsonString()}");
        Assert.Equal(agentId, step["agentName"]!.GetValue<string>());
        Assert.Contains("autofac-sandbox: task complete", step["output"]!.GetValue<string>(), StringComparison.Ordinal);

        var invocation = Assert.Single(step["toolInvocations"]!.AsArray().OfType<JsonObject>());
        Assert.Equal("sandbox.execute", invocation["toolName"]!.GetValue<string>());
        Assert.True(
            string.Equals(invocation["status"]!.GetValue<string>(), "completed", StringComparison.OrdinalIgnoreCase),
            $"Expected completed sandbox invocation but got '{invocation["status"]!.GetValue<string>()}'. Run: {run.ToJsonString()}");
        Assert.Equal("allow", invocation["policyDecisionKind"]!.GetValue<string>());
        Assert.Contains("\"sandbox_profile\":\"offline\"", invocation["inputSummary"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    private static bool IsTerminal(JsonObject run)
    {
        var status = run["status"]?.GetValue<string>() ?? string.Empty;
        return status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAgentMarkdown(string agentId, string action) =>
        $$"""
        ---
        id: {{agentId}}
        name: OpenSandbox E2E Agent
        description: Verifies that workflow service tasks execute through OpenSandbox.
        category: verification
        runner: agent-model
        tools: [sandbox.execute]
        deniedTools: []
        skills: []
        supportedActions: [{{action}}]
        supportedEnvironments: [ci]
        supportedPolicyTags: [opensandbox-e2e]
        sandboxProfiles: [offline]
        ---

        Run the requested verification task inside the configured sandbox provider.
        """;

    private static string BuildOpenSandboxWorkflow(string agentId, string action) =>
        $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:autofac="https://autofac.ai/bpmn"
                          id="opensandbox-e2e-defs"
                          targetNamespace="http://www.omg.org/spec/BPMN/20100524/MODEL">
          <bpmn:process id="opensandbox-e2e" name="OpenSandbox E2E Workflow" isExecutable="true">
            <bpmn:startEvent id="Start" name="Start"/>
            <bpmn:serviceTask id="RunInOpenSandbox" name="Run in OpenSandbox">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="{{agentId}}"
                  action="{{action}}"
                  environment="ci"
                  purposeType="verification"
                  policyTag="opensandbox-e2e"
                  sandboxProfile="offline"
                  permissionLevel="read-write"
                  allowedTools="sandbox.execute"/>
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:endEvent id="End" name="End"/>
          </bpmn:process>
        </bpmn:definitions>
        """;
}

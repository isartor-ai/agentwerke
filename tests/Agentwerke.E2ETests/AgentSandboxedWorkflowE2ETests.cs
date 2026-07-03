using System.Text.Json.Nodes;

namespace Agentwerke.E2ETests;

/// <summary>
/// Drives the real agent_sandboxed execution path end to end: a claude-code-runner
/// agent, dispatched through OpenSandboxedAgentRunner, running Agentwerke.AgentRunner.dll
/// inside a real container (the local Docker sandbox provider, used here purely so this
/// CI-safe test doesn't need a real OpenSandbox server — agent_sandboxed works the same
/// way against a real one, see docs/manual-test-opensandbox.md), calling a
/// WireMock-stubbed Anthropic Messages endpoint so no real model API key is needed.
///
/// This is deliberately separate from OpenSandboxWorkflowE2ETests, which exercises
/// tool_sandboxed (the generic sandbox.execute tool) rather than agent_sandboxed (a
/// model running inside the sandbox).
/// </summary>
public sealed class AgentSandboxedWorkflowE2ETests : E2ETestBase
{
    [AgentSandboxedFact]
    public async Task UploadClaudeCodeAgentThenRunWorkflow_ExecutesModelInsideSandbox()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(120));

        var suffix = Guid.NewGuid().ToString("N");
        var agentId = $"agent-sandboxed-e2e-{suffix}";
        var action = "spec.generate";

        var uploadedAgent = await Api.UploadAgentAsync(
            $"{agentId}.md",
            BuildAgentMarkdown(agentId, action));
        Assert.Equal(agentId, uploadedAgent["agentId"]!.GetValue<string>());

        var agent = await Api.GetAgentAsync(agentId);
        Assert.Equal("claude-code", agent["runner"]!.GetValue<string>());

        var bpmn = BuildAgentSandboxedWorkflow(agentId, action);
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
        Assert.Contains(
            "Specification generated successfully inside the sandbox",
            step["output"]!.GetValue<string>(),
            StringComparison.Ordinal);

        var runtimeSnapshot = step["runtimeSnapshot"]!.AsObject();
        Assert.Equal("agent_sandboxed", runtimeSnapshot["executionMode"]!.GetValue<string>());

        var sandboxExecution = runtimeSnapshot["sandboxExecution"]!.AsObject();
        Assert.Equal("docker", sandboxExecution["provider"]!.GetValue<string>());
        Assert.Equal("Completed", sandboxExecution["commandState"]!.GetValue<string>());
        Assert.Equal(0, sandboxExecution["exitCode"]!.GetValue<int>());

        var tokenUsage = runtimeSnapshot["tokenUsage"]!.AsObject();
        Assert.Equal(42, tokenUsage["inputTokens"]!.GetValue<int>());
        Assert.Equal(18, tokenUsage["outputTokens"]!.GetValue<int>());
        Assert.Equal("claude-sonnet-4-6", tokenUsage["modelId"]!.GetValue<string>());
    }

    /// <summary>
    /// Exercises the in-sandbox tool-calling loop (#130, #140): a two-turn conversation against
    /// the WireMock-stubbed Anthropic endpoint (see wiremock-anthropic-tool-use-stub.json) where
    /// the model calls sandbox.file_write, the runner executes it for real inside the container,
    /// and the model then reports success. This is what proves the new code-writing tools are
    /// actually wired into the sandboxed tool loop, not just unit-tested in isolation.
    /// </summary>
    [AgentSandboxedFact]
    public async Task UploadClaudeCodeAgentWithFileWriteTool_WritesAFileInsideTheSandbox()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(120));

        var suffix = Guid.NewGuid().ToString("N");
        var agentId = $"agent-sandboxed-tool-use-e2e-{suffix}";
        var action = "code.write_file";

        var uploadedAgent = await Api.UploadAgentAsync(
            $"{agentId}.md",
            BuildFileWriteAgentMarkdown(agentId, action));
        Assert.Equal(agentId, uploadedAgent["agentId"]!.GetValue<string>());

        var bpmn = BuildAgentSandboxedToolUseWorkflow(agentId, action);
        var workflowId = await Api.ImportWorkflowAsync($"{agentId}.bpmn", bpmn);
        await Api.PublishWorkflowAsync(workflowId, bpmn);

        var (runId, _) = await Api.StartRunAsync(workflowId);
        var run = await Api.PollRunUntilAsync(runId, IsTerminal, TimeSpan.FromSeconds(180));

        var status = run["status"]!.GetValue<string>();
        Assert.True(
            string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase),
            $"Expected completed run but got status '{status}'. Run: {run.ToJsonString()}");

        var step = Assert.Single(run["steps"]!.AsArray().OfType<JsonObject>());
        Assert.Contains(
            "File written successfully inside the sandbox",
            step["output"]!.GetValue<string>(),
            StringComparison.Ordinal);

        var runtimeSnapshot = step["runtimeSnapshot"]!.AsObject();
        var toolInvocations = runtimeSnapshot["toolInvocations"]!.AsArray().OfType<JsonObject>().ToArray();
        var fileWriteInvocation = Assert.Single(toolInvocations, t => t["toolName"]!.GetValue<string>() == "sandbox.file_write");
        Assert.Equal("completed", fileWriteInvocation["status"]!.GetValue<string>());
    }

    private static bool IsTerminal(JsonObject run)
    {
        var status = run["status"]?.GetValue<string>() ?? string.Empty;
        return status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFileWriteAgentMarkdown(string agentId, string action) =>
        $$"""
        ---
        id: {{agentId}}
        name: Agent Sandboxed Tool Use E2E Agent
        description: Verifies that the sandbox.file_write tool runs inside the agent_sandboxed tool loop.
        category: verification
        runner: claude-code
        tools: [sandbox.file_write]
        deniedTools: []
        skills: []
        supportedActions: [{{action}}]
        supportedEnvironments: [ci]
        supportedPolicyTags: [agent-sandboxed-e2e]
        sandboxProfiles: [repo-write]
        ---

        Write the requested file inside the sandbox workspace and report that it succeeded.
        """;

    private static string BuildAgentSandboxedToolUseWorkflow(string agentId, string action) =>
        $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:autofac="https://autofac.ai/bpmn"
                          id="agent-sandboxed-tool-use-e2e-defs"
                          targetNamespace="http://www.omg.org/spec/BPMN/20100524/MODEL">
          <bpmn:process id="agent-sandboxed-tool-use-e2e" name="Agent Sandboxed Tool Use E2E Workflow" isExecutable="true">
            <bpmn:startEvent id="Start" name="Start"/>
            <bpmn:serviceTask id="WriteFile" name="Write File">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="{{agentId}}"
                  action="{{action}}"
                  environment="ci"
                  purposeType="verification"
                  policyTag="agent-sandboxed-e2e"
                  executionMode="agent_sandboxed"
                  sandboxProfile="repo-write"
                  permissionLevel="read-write"/>
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:endEvent id="End" name="End"/>
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static string BuildAgentMarkdown(string agentId, string action) =>
        $$"""
        ---
        id: {{agentId}}
        name: Agent Sandboxed E2E Agent
        description: Verifies that workflow service tasks execute a model inside the sandbox (agent_sandboxed).
        category: verification
        runner: claude-code
        tools: []
        deniedTools: []
        skills: []
        supportedActions: [{{action}}]
        supportedEnvironments: [ci]
        supportedPolicyTags: [agent-sandboxed-e2e]
        sandboxProfiles: [offline]
        ---

        Generate the requested specification and report that it succeeded.
        """;

    private static string BuildAgentSandboxedWorkflow(string agentId, string action) =>
        $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:autofac="https://autofac.ai/bpmn"
                          id="agent-sandboxed-e2e-defs"
                          targetNamespace="http://www.omg.org/spec/BPMN/20100524/MODEL">
          <bpmn:process id="agent-sandboxed-e2e" name="Agent Sandboxed E2E Workflow" isExecutable="true">
            <bpmn:startEvent id="Start" name="Start"/>
            <bpmn:serviceTask id="GenerateSpec" name="Generate Specification">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="{{agentId}}"
                  action="{{action}}"
                  environment="ci"
                  purposeType="verification"
                  policyTag="agent-sandboxed-e2e"
                  executionMode="agent_sandboxed"
                  sandboxProfile="offline"
                  permissionLevel="read-write"/>
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:endEvent id="End" name="End"/>
          </bpmn:process>
        </bpmn:definitions>
        """;
}

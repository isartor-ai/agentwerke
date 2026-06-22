using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Models;

public sealed class SandboxedAgentRuntimeExecutor
{
    private readonly IAgentModelRunner _runner;

    public SandboxedAgentRuntimeExecutor(IAgentModelRunner runner)
    {
        _runner = runner;
    }

    public async Task<SandboxedAgentRunResult> ExecuteAsync(
        SandboxedAgentRunEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.Contract.McpServers.Count > 0)
        {
            return new SandboxedAgentRunResult(
                Succeeded: false,
                Output: null,
                FailureReason: "Sandboxed agent runtime does not support MCP servers yet.",
                TokenUsage: null,
                Artifacts: null,
                ToolInvocations: []);
        }

        if (envelope.Contract.SubAgents?.Enabled == true)
        {
            return new SandboxedAgentRunResult(
                Succeeded: false,
                Output: null,
                FailureReason: "Sandboxed agent runtime does not support sub-agents yet.",
                TokenUsage: null,
                Artifacts: null,
                ToolInvocations: []);
        }

        var result = await _runner.RunAsync(
            new ModelRunRequest(
                RunId: envelope.RunId,
                StepId: envelope.StepId,
                AgentName: envelope.AgentName,
                Action: envelope.Action,
                Environment: envelope.Environment,
                PurposeType: envelope.PurposeType,
                PolicyTag: envelope.PolicyTag,
                RequiresEvidence: [],
                Attempt: envelope.Attempt,
                PromptSnapshot: new AgentPromptSnapshot(
                    envelope.UserPrompt,
                    DateTimeOffset.UtcNow.ToString("o"),
                    [],
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    []),
                Contract: envelope.Contract),
            cancellationToken);

        return new SandboxedAgentRunResult(
            Succeeded: result.Succeeded,
            Output: result.Output,
            FailureReason: result.FailureReason,
            TokenUsage: result.TokenUsage,
            Artifacts: result.Artifacts,
            ToolInvocations: result.ToolInvocations);
    }
}

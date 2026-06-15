using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Hooks;

public sealed class InternalPolicyHookHandler : IAgentHookHandler
{
    public string Type => "internal-policy";

    public Task<HookHandlerResult> ExecuteAsync(
        AgentHookContract hook,
        AgentHookContext context,
        CancellationToken cancellationToken)
    {
        var decision = hook.Settings.TryGetValue("decision", out var configuredDecision)
            ? configuredDecision
            : AgentHookDecisions.Proceed;

        var output = hook.Settings.TryGetValue("output", out var configuredOutput)
            ? configuredOutput
            : null;

        var reason = hook.Settings.TryGetValue("reason", out var configuredReason)
            ? configuredReason
            : null;

        return Task.FromResult(new HookHandlerResult(decision, output, reason));
    }
}

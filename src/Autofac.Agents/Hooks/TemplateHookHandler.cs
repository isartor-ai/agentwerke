using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Hooks;

public sealed class TemplateHookHandler : IAgentHookHandler
{
    public string Type => "template";

    public Task<HookHandlerResult> ExecuteAsync(
        AgentHookContract hook,
        AgentHookContext context,
        CancellationToken cancellationToken)
    {
        var decisionTemplate = hook.Settings.TryGetValue("decision", out var decision)
            ? decision
            : AgentHookDecisions.Proceed;

        var outputTemplate = hook.Settings.TryGetValue("output", out var output)
            ? output
            : null;

        var reasonTemplate = hook.Settings.TryGetValue("reason", out var reason)
            ? reason
            : null;

        return Task.FromResult(new HookHandlerResult(
            Render(decisionTemplate, context.Values) ?? AgentHookDecisions.Proceed,
            Render(outputTemplate, context.Values),
            Render(reasonTemplate, context.Values)));
    }

    private static string? Render(string? template, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return template;
        }

        var rendered = template;
        foreach (var pair in values)
        {
            rendered = rendered.Replace($"{{{{{pair.Key}}}}}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return rendered;
    }
}

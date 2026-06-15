using System.Diagnostics;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Hooks;

public sealed class HookGateway : IAgentHookGateway
{
    private readonly IReadOnlyDictionary<string, IAgentHookHandler> _handlers;

    public HookGateway(IEnumerable<IAgentHookHandler> handlers)
    {
        _handlers = handlers.ToDictionary(static handler => handler.Type, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<HookDispatchResult> ExecuteAsync(
        string eventName,
        IReadOnlyList<AgentHookContract> hooks,
        AgentHookContext context,
        CancellationToken cancellationToken)
    {
        var matchingHooks = (hooks ?? [])
            .Where(hook => string.Equals(hook.Event, eventName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingHooks.Length == 0)
        {
            return new HookDispatchResult(AgentHookDecisions.Proceed, null, null, []);
        }

        var records = new List<AgentHookExecutionRecord>();
        foreach (var hook in matchingHooks)
        {
            var stopwatch = Stopwatch.StartNew();

            if (!_handlers.TryGetValue(hook.Type, out var handler))
            {
                stopwatch.Stop();
                var failureReason = $"Hook '{hook.Name}' uses unsupported type '{hook.Type}'.";
                var missingRecord = CreateRecord(hook, AgentHookDecisions.Block, null, failureReason, (int)stopwatch.ElapsedMilliseconds);
                if (ShouldFailOpen(hook))
                {
                    records.Add(missingRecord with { Decision = AgentHookDecisions.FailOpen });
                    continue;
                }

                records.Add(missingRecord);
                return new HookDispatchResult(AgentHookDecisions.Block, null, failureReason, records);
            }

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, hook.TimeoutSeconds)));

                var result = await handler.ExecuteAsync(hook, context, timeoutCts.Token);
                stopwatch.Stop();

                var record = CreateRecord(
                    hook,
                    NormalizeDecision(result.Decision),
                    result.OutputSummary,
                    result.FailureReason,
                    (int)stopwatch.ElapsedMilliseconds);
                records.Add(record);

                if (!string.Equals(record.Decision, AgentHookDecisions.Proceed, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(record.Decision, AgentHookDecisions.FailOpen, StringComparison.OrdinalIgnoreCase))
                {
                    return new HookDispatchResult(record.Decision, record.OutputSummary, record.ErrorMessage, records);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                if (ShouldFailOpen(hook))
                {
                    records.Add(CreateRecord(
                        hook,
                        AgentHookDecisions.FailOpen,
                        null,
                        ex.Message,
                        (int)stopwatch.ElapsedMilliseconds));
                    continue;
                }

                var failure = CreateRecord(
                    hook,
                    AgentHookDecisions.Block,
                    null,
                    ex.Message,
                    (int)stopwatch.ElapsedMilliseconds);
                records.Add(failure);
                return new HookDispatchResult(AgentHookDecisions.Block, null, ex.Message, records);
            }
        }

        return new HookDispatchResult(AgentHookDecisions.Proceed, null, null, records);
    }

    private static string NormalizeDecision(string? decision)
    {
        return decision?.Trim().ToLowerInvariant() switch
        {
            AgentHookDecisions.Block => AgentHookDecisions.Block,
            AgentHookDecisions.Skip => AgentHookDecisions.Skip,
            AgentHookDecisions.Override => AgentHookDecisions.Override,
            AgentHookDecisions.FailOpen => AgentHookDecisions.FailOpen,
            _ => AgentHookDecisions.Proceed
        };
    }

    private static bool ShouldFailOpen(AgentHookContract hook) =>
        string.Equals(hook.FailureMode, AgentHookFailureModes.FailOpen, StringComparison.OrdinalIgnoreCase);

    private static AgentHookExecutionRecord CreateRecord(
        AgentHookContract hook,
        string decision,
        string? outputSummary,
        string? errorMessage,
        int durationMs) =>
        new()
        {
            HookName = hook.Name,
            Event = hook.Event,
            Type = hook.Type,
            Decision = decision,
            Blocking = hook.Blocking,
            OutputSummary = outputSummary,
            ErrorMessage = errorMessage,
            DurationMs = durationMs
        };
}

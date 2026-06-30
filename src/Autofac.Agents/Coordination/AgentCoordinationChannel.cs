using System.Collections.Concurrent;

namespace Autofac.Agents.Coordination;

public sealed record AgentCoordinationMessage(string From, string Text, string CreatedAt);

/// <summary>
/// Run-scoped, append-only channel agents use to coordinate — e.g. one agent posts a result
/// or a "waiting on X" note and another reads it (#173). Thread-safe; ordered by post time.
/// In-memory for now; persisting alongside the run history is a follow-up.
/// </summary>
public interface IAgentCoordinationChannel
{
    AgentCoordinationMessage Post(string runId, string from, string text);

    IReadOnlyList<AgentCoordinationMessage> Read(string runId, string? fromFilter = null);
}

public sealed class InMemoryAgentCoordinationChannel : IAgentCoordinationChannel
{
    private readonly ConcurrentDictionary<string, List<AgentCoordinationMessage>> _byRun = new(StringComparer.Ordinal);

    public AgentCoordinationMessage Post(string runId, string from, string text)
    {
        var message = new AgentCoordinationMessage(
            string.IsNullOrWhiteSpace(from) ? "agent" : from,
            text ?? string.Empty,
            DateTimeOffset.UtcNow.ToString("o"));

        var messages = _byRun.GetOrAdd(runId, static _ => []);
        lock (messages)
        {
            messages.Add(message);
        }

        return message;
    }

    public IReadOnlyList<AgentCoordinationMessage> Read(string runId, string? fromFilter = null)
    {
        if (!_byRun.TryGetValue(runId, out var messages))
        {
            return [];
        }

        lock (messages)
        {
            IEnumerable<AgentCoordinationMessage> view = messages;
            if (!string.IsNullOrWhiteSpace(fromFilter))
            {
                view = view.Where(m => string.Equals(m.From, fromFilter, StringComparison.OrdinalIgnoreCase));
            }

            return view.ToArray();
        }
    }
}

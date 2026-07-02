using System.Collections.Concurrent;
using Autofac.Application.Agents;
using Autofac.Domain.Persistence;

namespace Autofac.Agents.Coordination;

public sealed record AgentCoordinationMessage(string From, string Text, string CreatedAt);

/// <summary>
/// Run-scoped, append-only channel agents use to coordinate — e.g. one agent posts a result
/// or a "waiting on X" note and another reads it (#173). Ordered by post time. Backed by the
/// persisted <see cref="AgentInteraction"/> store (#192) so messages survive restart and feed
/// the run Conversation view; <see cref="InMemoryAgentCoordinationChannel"/> is kept for tests.
/// </summary>
public interface IAgentCoordinationChannel
{
    Task<AgentCoordinationMessage> PostAsync(
        string runId,
        string from,
        string text,
        string? stepId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentCoordinationMessage>> ReadAsync(
        string runId,
        string? fromFilter = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists coordination messages as <see cref="AgentInteractionKinds.Post"/> interactions so
/// they survive restart and appear in the unified run conversation (#192).
/// </summary>
public sealed class PersistentAgentCoordinationChannel : IAgentCoordinationChannel
{
    private readonly IAgentInteractionRepository _repository;

    public PersistentAgentCoordinationChannel(IAgentInteractionRepository repository) =>
        _repository = repository;

    public async Task<AgentCoordinationMessage> PostAsync(
        string runId,
        string from,
        string text,
        string? stepId = null,
        CancellationToken cancellationToken = default)
    {
        var sender = string.IsNullOrWhiteSpace(from) ? "agent" : from;
        var createdAt = DateTimeOffset.UtcNow.ToString("o");

        var interaction = new AgentInteraction
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = runId,
            StepId = string.IsNullOrWhiteSpace(stepId) ? null : stepId,
            FromAgent = sender,
            Kind = AgentInteractionKinds.Post,
            AddresseeType = AgentInteractionAddresseeTypes.Agent,
            Addressee = null,
            Blocking = false,
            Prompt = text ?? string.Empty,
            Status = AgentInteractionStatuses.Posted,
            CreatedAt = createdAt,
        };

        await _repository.AddAsync(interaction, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new AgentCoordinationMessage(sender, interaction.Prompt, createdAt);
    }

    public async Task<IReadOnlyList<AgentCoordinationMessage>> ReadAsync(
        string runId,
        string? fromFilter = null,
        CancellationToken cancellationToken = default)
    {
        var posts = await _repository.GetPostsForRunAsync(runId, fromFilter, cancellationToken);
        return posts
            .Select(p => new AgentCoordinationMessage(p.FromAgent, p.Prompt, p.CreatedAt))
            .ToArray();
    }
}

/// <summary>In-memory channel for unit tests and offline scenarios; not persisted.</summary>
public sealed class InMemoryAgentCoordinationChannel : IAgentCoordinationChannel
{
    private readonly ConcurrentDictionary<string, List<AgentCoordinationMessage>> _byRun = new(StringComparer.Ordinal);

    public Task<AgentCoordinationMessage> PostAsync(
        string runId,
        string from,
        string text,
        string? stepId = null,
        CancellationToken cancellationToken = default)
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

        return Task.FromResult(message);
    }

    public Task<IReadOnlyList<AgentCoordinationMessage>> ReadAsync(
        string runId,
        string? fromFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (!_byRun.TryGetValue(runId, out var messages))
        {
            return Task.FromResult<IReadOnlyList<AgentCoordinationMessage>>([]);
        }

        lock (messages)
        {
            IEnumerable<AgentCoordinationMessage> view = messages;
            if (!string.IsNullOrWhiteSpace(fromFilter))
            {
                view = view.Where(m => string.Equals(m.From, fromFilter, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<AgentCoordinationMessage>>(view.ToArray());
        }
    }
}

using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;

namespace Autofac.Agents.Tests;

/// <summary>Test double for <see cref="IRunContextRepository"/> backed by a dictionary.</summary>
internal sealed class InMemoryRunContextRepository : IRunContextRepository
{
    private readonly Dictionary<string, RunContextEntry> _entries = new(StringComparer.Ordinal);

    public Task SetAsync(string runId, string key, string value, string kind, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        _entries[$"{runId}::{key}"] = new RunContextEntry
        {
            Id = $"ctx_{Guid.NewGuid():N}",
            RunId = runId,
            Key = key,
            Value = value,
            Kind = kind,
            CreatedAt = now,
            UpdatedAt = now
        };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RunContextEntry>> GetAllAsync(string runId, CancellationToken cancellationToken)
    {
        var result = _entries.Values
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.CreatedAt, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<RunContextEntry>>(result);
    }
}

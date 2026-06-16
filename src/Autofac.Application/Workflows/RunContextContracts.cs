using Autofac.Domain.Persistence;

namespace Autofac.Application.Workflows;

/// <summary>
/// Run-scoped key/value context, used to flow data between tasks in a workflow
/// (e.g. the triggering issue into the first agent, and each step's output into
/// later steps). Implemented in Autofac.Infrastructure.
/// </summary>
public interface IRunContextRepository
{
    /// <summary>Upserts a context entry. Writing an existing key replaces its value.</summary>
    Task SetAsync(string runId, string key, string value, string kind, CancellationToken cancellationToken);

    /// <summary>Returns all context entries for a run, ordered by creation time.</summary>
    Task<IReadOnlyList<RunContextEntry>> GetAllAsync(string runId, CancellationToken cancellationToken);
}

/// <summary>Well-known run-context key prefixes.</summary>
public static class RunContextKinds
{
    public const string Input = "input";
    public const string Output = "output";
}

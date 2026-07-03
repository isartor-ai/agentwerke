namespace Agentwerke.Domain.Persistence;

/// <summary>
/// A single key/value entry in a workflow run's shared context bag.
/// Used to carry data between tasks — e.g. the triggering issue ("input.*")
/// and each completed step's primary output ("output.&lt;nodeId&gt;").
/// Keys are unique per run; writing an existing key replaces its value.
/// </summary>
public sealed class RunContextEntry
{
    public string Id { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    /// <summary>Stable key, e.g. "input.body" or "output.RunAnalysis".</summary>
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>Classifier for the entry, e.g. "input" or "output".</summary>
    public string Kind { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;

    public string UpdatedAt { get; set; } = string.Empty;
}

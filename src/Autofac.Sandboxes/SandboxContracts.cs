namespace Autofac.Sandboxes;

/// <summary>
/// All inputs needed to run one agent task inside a sandbox container.
/// </summary>
public sealed record SandboxExecutionRequest(
    string RunId,
    string StepId,
    string AgentName,
    string Action,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    int Attempt,
    /// <summary>Docker image to run. Falls back to <see cref="SandboxOptions.DefaultImage"/> when null.</summary>
    string? Image = null);

/// <summary>
/// Outcome produced after the container exits (or is timed out / fails to start).
/// </summary>
public sealed record SandboxExecutionResult(
    bool Succeeded,
    string Logs,
    string? FailureReason,
    /// <summary>
    /// Files written to /output inside the container, keyed by file name.
    /// Empty when the container produced no output files.
    /// </summary>
    IReadOnlyDictionary<string, string> Artifacts,
    int? ExitCode,
    TimeSpan Duration);

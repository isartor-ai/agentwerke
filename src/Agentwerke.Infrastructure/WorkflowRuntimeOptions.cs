using Microsoft.Extensions.Configuration;

namespace Agentwerke.Infrastructure;

/// <summary>
/// Selects which workflow execution runtime Agentwerke uses. The bounded, Postgres-backed
/// <see cref="WorkflowRuntimeMode.Agentwerke"/> runtime is the default; <see cref="WorkflowRuntimeMode.Camunda"/>
/// is an explicit, opt-in enterprise adapter (see ADR-002).
/// </summary>
public enum WorkflowRuntimeMode
{
    Agentwerke,
    Camunda
}

public sealed class WorkflowRuntimeOptions
{
    public const string Section = "WorkflowRuntime";

    public WorkflowRuntimeMode Mode { get; set; } = WorkflowRuntimeMode.Agentwerke;

    public bool IsCamundaMode => Mode == WorkflowRuntimeMode.Camunda;

    /// <summary>
    /// Resolves the active runtime mode from configuration, defaulting to <see cref="WorkflowRuntimeMode.Agentwerke"/>
    /// when unset and failing fast with an actionable error when an unsupported value is supplied.
    /// </summary>
    public static WorkflowRuntimeOptions Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var raw = configuration.GetSection(Section)["Mode"];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new WorkflowRuntimeOptions { Mode = WorkflowRuntimeMode.Agentwerke };
        }

        var normalized = raw.Trim();
        if (Enum.TryParse<WorkflowRuntimeMode>(normalized, ignoreCase: true, out var mode)
            && Enum.IsDefined(mode))
        {
            return new WorkflowRuntimeOptions { Mode = mode };
        }

        var supported = string.Join(", ", Enum.GetNames<WorkflowRuntimeMode>());
        throw new InvalidOperationException(
            $"Unsupported workflow runtime mode '{raw}'. " +
            $"Set '{Section}:Mode' to one of: {supported}.");
    }
}

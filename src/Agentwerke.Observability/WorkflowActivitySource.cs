using System.Diagnostics;

namespace Agentwerke.Observability;

/// <summary>
/// Shared ActivitySource for all Autofac workflow spans.
/// Consume via DI: <c>services.AddSingleton(WorkflowActivitySource.Instance)</c>
/// or resolve <see cref="ActivitySource"/> from the container.
/// </summary>
public static class WorkflowActivitySource
{
    public const string SourceName = "Agentwerke.Workflows";

    public static readonly ActivitySource Instance = new(SourceName, "1.0.0");
}

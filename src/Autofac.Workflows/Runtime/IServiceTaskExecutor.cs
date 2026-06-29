using Autofac.Domain.AgentRuntime;
using Autofac.Domain.Persistence;
using Autofac.Workflows.Bpmn;

namespace Autofac.Workflows.Runtime;

public interface IServiceTaskExecutor
{
    Task<AgentTaskOutcome> ExecuteAsync(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        int attempt,
        CancellationToken cancellationToken);
}

public static class AgentTaskOutcomeStatuses
{
    public const string NeedsConfig = "needs_config";
}

public sealed record AgentTaskOutcome(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    IReadOnlyDictionary<string, string>? Artifacts = null,
    IReadOnlyList<ExternalActionRecord>? ExternalActions = null,
    PolicyDecision? PolicyDecision = null,
    AgentRuntimeSnapshot? RuntimeSnapshot = null,
    string? RoutingDirective = null,
    IReadOnlyDictionary<string, string>? ContextUpdates = null,
    IReadOnlyList<string>? FilesTouched = null,
    string? StepStatus = null);

public sealed record ExternalActionRecord(
    string Provider,
    string Action,
    string Status,
    string? ResourceId = null,
    string? ResourceUrl = null,
    string? Summary = null);

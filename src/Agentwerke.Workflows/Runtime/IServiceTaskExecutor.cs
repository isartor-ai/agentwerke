using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;
using Agentwerke.Workflows.Bpmn;

namespace Agentwerke.Workflows.Runtime;

public interface IServiceTaskExecutor
{
    Task<AgentTaskOutcome> ExecuteAsync(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        int attempt,
        CancellationToken cancellationToken,
        AgentExecutionProgressReporter? progressReporter = null);
}

public static class AgentTaskOutcomeStatuses
{
    public const string NeedsConfig = "needs_config";

    /// <summary>The run reached its configured model cost/token budget (#175).</summary>
    public const string BudgetExceeded = "budget_exceeded";

    /// <summary>
    /// The agent paused mid-step to ask a human and is waiting for an answer (#192).
    /// The engine suspends the run (waiting_user) and re-runs this step on resume.
    /// </summary>
    public const string WaitingUser = "waiting_user";
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
    string? Summary = null,
    /// <summary>
    /// The key by which this action's outcome will be correlated back to the run, for actions whose
    /// result arrives later as an external event (#210). A CI dispatch has no external id to record —
    /// GitHub's workflow_dispatch answers 204 with no run id — so the correlation key is the only
    /// identifier tying the dispatch to the callback that resumes the run. Structured rather than
    /// prose in Summary, so evidence can be audited on it.
    /// </summary>
    string? CorrelationKey = null);

using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Hooks;

public static class AgentHookEvents
{
    public const string BeforeAgentRun = "before_agent_run";
    public const string AfterAgentRun = "after_agent_run";
    public const string BeforeToolCall = "before_tool_call";
    public const string AfterToolCall = "after_tool_call";
    public const string OnArtifactCreated = "on_artifact_created";
    public const string OnFailure = "on_failure";
}

public static class AgentHookDecisions
{
    public const string Proceed = "proceed";
    public const string Skip = "skip";
    public const string Block = "block";
    public const string Override = "override";
    public const string FailOpen = "fail-open";
}

public sealed record AgentHookContext(
    string RunId,
    string StepId,
    string NodeId,
    string NodeName,
    string AgentName,
    string Action,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    int Attempt,
    IReadOnlyDictionary<string, string> Values);

public sealed record HookHandlerResult(
    string Decision,
    string? OutputSummary = null,
    string? FailureReason = null);

public interface IAgentHookHandler
{
    string Type { get; }

    Task<HookHandlerResult> ExecuteAsync(
        AgentHookContract hook,
        AgentHookContext context,
        CancellationToken cancellationToken);
}

public sealed record HookDispatchResult(
    string Decision,
    string? OutputSummary,
    string? FailureReason,
    IReadOnlyList<AgentHookExecutionRecord> Records);

public interface IAgentHookGateway
{
    Task<HookDispatchResult> ExecuteAsync(
        string eventName,
        IReadOnlyList<AgentHookContract> hooks,
        AgentHookContext context,
        CancellationToken cancellationToken);
}

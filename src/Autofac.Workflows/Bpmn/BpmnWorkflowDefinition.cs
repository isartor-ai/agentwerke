using Autofac.Domain.AgentRuntime;

namespace Autofac.Workflows.Bpmn;

public sealed record BpmnWorkflowDefinition(
    string ProcessId,
    string? ProcessName,
    IReadOnlyList<BpmnNodeDefinition> Nodes,
    IReadOnlyList<BpmnSequenceFlow>? SequenceFlows = null);

public sealed record BpmnSequenceFlow(
    string Id,
    string SourceRef,
    string TargetRef,
    string? ConditionExpression = null);

public sealed record BpmnNodeDefinition(
    string Id,
    string? Name,
    string ElementName,
    AutofacTaskMetadata? Metadata,
    AutofacApprovalMetadata? ApprovalMetadata = null,
    string? TimerDuration = null);

public sealed record AutofacTaskMetadata(
    string Agent,
    string Action,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    IReadOnlyList<string> RequiresEvidence,
    int MaxRetries = 0,
    int RetryBackoffSeconds = 0,
    int FailUntilAttempt = 0,
    bool SimulateTimeout = false,
    int? TimeoutSeconds = null,
    AgentRuntimeContract? RuntimeContract = null,
    string? ExecutionMode = null,
    /// <summary>
    /// Named sandbox profile (e.g. "repo-write", "deployment") this task requests. Null
    /// defaults to the most restrictive "offline" profile. Validated against the agent's
    /// declared allow-list and policy risk level before the sandbox is created.
    /// </summary>
    string? SandboxProfile = null);

public sealed record AutofacApprovalMetadata(
    string PurposeType,
    string PolicyTag);

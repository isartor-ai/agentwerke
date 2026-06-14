using Autofac.Domain.AgentRuntime;

namespace Autofac.Workflows.Bpmn;

public sealed record BpmnWorkflowDefinition(
    string ProcessId,
    string? ProcessName,
    IReadOnlyList<BpmnNodeDefinition> Nodes);

public sealed record BpmnNodeDefinition(
    string Id,
    string? Name,
    string ElementName,
    AutofacTaskMetadata? Metadata,
    AutofacApprovalMetadata? ApprovalMetadata = null);

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
    AgentRuntimeContract? RuntimeContract = null);

public sealed record AutofacApprovalMetadata(
    string PurposeType,
    string PolicyTag);

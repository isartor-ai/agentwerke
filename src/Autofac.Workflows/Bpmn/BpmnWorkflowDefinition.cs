using Autofac.Domain.AgentRuntime;

namespace Autofac.Workflows.Bpmn;

public sealed record BpmnWorkflowDefinition(
    string ProcessId,
    string? ProcessName,
    IReadOnlyList<BpmnNodeDefinition> Nodes,
    IReadOnlyList<BpmnSequenceFlow>? SequenceFlows = null)
{
    /// <summary>
    /// Walks backward in document order from <paramref name="fromNodeId"/> to find the
    /// nearest preceding agent-driven service task — used to attribute a userTask approval
    /// gate (agent name, produced artifact) to the step that triggered it.
    /// </summary>
    public BpmnNodeDefinition? FindPrecedingServiceTaskNode(string fromNodeId)
    {
        var fromIndex = -1;
        for (var i = 0; i < Nodes.Count; i++)
        {
            if (string.Equals(Nodes[i].Id, fromNodeId, StringComparison.Ordinal))
            {
                fromIndex = i;
                break;
            }
        }

        for (var i = fromIndex - 1; i >= 0; i--)
        {
            var node = Nodes[i];
            if ((node.ElementName is "serviceTask" or "scriptTask") && node.Metadata?.Agent is not null)
            {
                return node;
            }
        }

        return null;
    }
}

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
    string? TimerDuration = null,
    AutofacExternalEventMetadata? ExternalEventMetadata = null);

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
    string? SandboxProfile = null,
    /// <summary>
    /// For a github.create_pull_request task: when true, include prior agent step
    /// output(s) from run context in the PR (committed file + body), so the PR contains
    /// the agent's work, not just run metadata (#150).
    /// </summary>
    bool IncludeAgentOutput = false,
    /// <summary>
    /// Optional node id whose output (<c>output.&lt;id&gt;</c>) to include when
    /// <see cref="IncludeAgentOutput"/> is set. When null, all <c>output.*</c> entries
    /// are included.
    /// </summary>
    string? OutputFrom = null);

public sealed record AutofacApprovalMetadata(
    string PurposeType,
    string PolicyTag);

public sealed record AutofacExternalEventMetadata(
    string MessageName,
    string CorrelationKeyTemplate);

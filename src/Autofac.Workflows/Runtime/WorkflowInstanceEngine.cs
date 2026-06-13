using System.Text.Json;
using Autofac.Workflows.Bpmn;

namespace Autofac.Workflows.Runtime;

public sealed class WorkflowInstanceEngine : IWorkflowInstanceEngine
{
    private const string RunningStatus = "running";
    private const string WaitingUserStatus = "waiting_user";
    private const string CompletedStatus = "completed";
    private const string FailedStatus = "failed";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkflowRuntimeStore _store;
    private readonly IServiceTaskExecutor _serviceTaskExecutor;

    public WorkflowInstanceEngine(IWorkflowRuntimeStore store, IServiceTaskExecutor serviceTaskExecutor)
    {
        _store = store;
        _serviceTaskExecutor = serviceTaskExecutor;
    }

    public async Task<WorkflowExecutionState> StartAsync(
        string workflowDefinitionId,
        BpmnWorkflowDefinition definition,
        string? initiator,
        CancellationToken cancellationToken)
    {
        ValidateDefinition(definition);

        var run = await _store.CreateRunAsync(workflowDefinitionId, initiator, cancellationToken);

        await _store.AppendEventAsync(
            run.Id,
            "run_started",
            Serialize(new { runId = run.Id, workflowDefinitionId, initiator }),
            cancellationToken);

        return await AdvanceAsync(
            run.Id,
            definition,
            startIndex: 0,
            startStatus: RunningStatus,
            cancellationToken);
    }

    public async Task<WorkflowExecutionState> ResumeAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        string? approvedBy,
        CancellationToken cancellationToken)
    {
        ValidateDefinition(definition);

        var checkpoint = await GetCheckpointAsync(runId, cancellationToken);
        if (checkpoint is null)
        {
            throw new InvalidOperationException($"No persisted checkpoint exists for run '{runId}'.");
        }

        if (!string.Equals(checkpoint.Status, WaitingUserStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Run '{runId}' is not waiting for user input.");
        }

        await _store.AppendEventAsync(
            runId,
            "user_task_completed",
            Serialize(new
            {
                runId,
                nodeId = checkpoint.WaitingOnNodeId,
                approvedBy,
                timestampUtc = DateTime.UtcNow.ToString("o")
            }),
            cancellationToken);

        return await AdvanceAsync(
            runId,
            definition,
            startIndex: checkpoint.NextNodeIndex,
            startStatus: RunningStatus,
            cancellationToken);
    }

    public async Task<WorkflowExecutionState> RecoverAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        CancellationToken cancellationToken)
    {
        ValidateDefinition(definition);

        _ = await _store.GetRunAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow run '{runId}' was not found.");

        var checkpoint = await GetCheckpointAsync(runId, cancellationToken);
        if (checkpoint is null)
        {
            throw new InvalidOperationException($"No persisted checkpoint exists for run '{runId}'.");
        }

        if (string.Equals(checkpoint.Status, CompletedStatus, StringComparison.Ordinal))
        {
            return new WorkflowExecutionState(
                RunId: runId,
                Status: CompletedStatus,
                NextNodeIndex: checkpoint.NextNodeIndex,
                WaitingOnNodeId: null,
                CompletedAt: checkpoint.CompletedAt);
        }

        if (string.Equals(checkpoint.Status, WaitingUserStatus, StringComparison.Ordinal))
        {
            return new WorkflowExecutionState(
                RunId: runId,
                Status: WaitingUserStatus,
                NextNodeIndex: checkpoint.NextNodeIndex,
                WaitingOnNodeId: checkpoint.WaitingOnNodeId,
                CompletedAt: checkpoint.CompletedAt);
        }

        return await AdvanceAsync(
            runId,
            definition,
            startIndex: checkpoint.NextNodeIndex,
            startStatus: RunningStatus,
            cancellationToken);
    }

    private async Task<WorkflowExecutionState> AdvanceAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        int startIndex,
        string startStatus,
        CancellationToken cancellationToken)
    {
        var nextIndex = startIndex;
        var status = startStatus;

        await _store.UpdateRunStatusAsync(runId, RunningStatus, completedAt: null, cancellationToken);

        while (nextIndex < definition.Nodes.Count)
        {
            var node = definition.Nodes[nextIndex];

            if (!IsSupportedRuntimeNode(node.ElementName))
            {
                throw new InvalidOperationException(
                    $"Runtime execution for node '{node.Id}' with type '{node.ElementName}' is not supported in Phase 2.2.");
            }

            await _store.AppendEventAsync(
                runId,
                "node_entered",
                Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, nodeIndex = nextIndex }),
                cancellationToken);

            switch (node.ElementName)
            {
                case "startEvent":
                    await CompleteNodeAndCheckpointAsync(runId, node, nextIndex, cancellationToken);
                    nextIndex++;
                    break;
                case "serviceTask":
                    var serviceResult = await ExecuteServiceTaskAsync(
                        runId,
                        definition,
                        node,
                        nextIndex,
                        cancellationToken);

                    if (serviceResult == ServiceExecutionResult.Failed)
                    {
                        await _store.UpdateRunStatusAsync(runId, FailedStatus, completedAt: null, cancellationToken);
                        await SaveCheckpointAsync(runId, FailedStatus, nextIndex, waitingOnNodeId: null, completedAt: null, cancellationToken);

                        return new WorkflowExecutionState(
                            RunId: runId,
                            Status: FailedStatus,
                            NextNodeIndex: nextIndex,
                            WaitingOnNodeId: null,
                            CompletedAt: null);
                    }

                    nextIndex++;
                    await SaveCheckpointAsync(runId, RunningStatus, nextIndex, waitingOnNodeId: null, completedAt: null, cancellationToken);
                    break;
                case "userTask":
                    status = WaitingUserStatus;
                    await _store.AppendEventAsync(
                        runId,
                        "user_task_waiting",
                        Serialize(new
                        {
                            runId,
                            nodeId = node.Id,
                            nodeIndex = nextIndex,
                            purposeType = node.ApprovalMetadata?.PurposeType,
                            policyTag = node.ApprovalMetadata?.PolicyTag
                        }),
                        cancellationToken);
                    nextIndex++;
                    await _store.UpdateRunStatusAsync(runId, status, completedAt: null, cancellationToken);
                    await SaveCheckpointAsync(runId, status, nextIndex, waitingOnNodeId: node.Id, completedAt: null, cancellationToken);

                    return new WorkflowExecutionState(
                        RunId: runId,
                        Status: status,
                        NextNodeIndex: nextIndex,
                        WaitingOnNodeId: node.Id,
                        CompletedAt: null);
                case "exclusiveGateway":
                    await _store.AppendEventAsync(
                        runId,
                        "gateway_evaluated",
                        Serialize(new
                        {
                            runId,
                            gatewayId = node.Id,
                            gatewayType = "exclusive",
                            selectedPath = "default"
                        }),
                        cancellationToken);
                    await CompleteNodeAndCheckpointAsync(runId, node, nextIndex, cancellationToken);
                    nextIndex++;
                    break;
                case "parallelGateway":
                    var joinIndex = FindParallelJoinIndex(definition, nextIndex);
                    if (joinIndex > nextIndex)
                    {
                        var branchNodes = definition.Nodes.Skip(nextIndex + 1).Take(joinIndex - nextIndex - 1).ToArray();
                        await _store.AppendEventAsync(
                            runId,
                            "parallel_forked",
                            Serialize(new
                            {
                                runId,
                                gatewayId = node.Id,
                                branchNodeIds = branchNodes.Select(static b => b.Id)
                            }),
                            cancellationToken);

                        foreach (var branchNode in branchNodes)
                        {
                            await _store.AppendEventAsync(
                                runId,
                                "parallel_branch_entered",
                                Serialize(new { runId, branchNodeId = branchNode.Id, branchNodeType = branchNode.ElementName }),
                                cancellationToken);

                            if (branchNode.ElementName == "serviceTask")
                            {
                                var branchResult = await ExecuteServiceTaskAsync(
                                    runId,
                                    definition,
                                    branchNode,
                                    nextIndex,
                                    cancellationToken);

                                if (branchResult == ServiceExecutionResult.Failed)
                                {
                                    await _store.UpdateRunStatusAsync(runId, FailedStatus, completedAt: null, cancellationToken);
                                    await SaveCheckpointAsync(runId, FailedStatus, nextIndex, waitingOnNodeId: null, completedAt: null, cancellationToken);

                                    return new WorkflowExecutionState(
                                        RunId: runId,
                                        Status: FailedStatus,
                                        NextNodeIndex: nextIndex,
                                        WaitingOnNodeId: null,
                                        CompletedAt: null);
                                }
                            }
                            else if (branchNode.ElementName == "intermediateCatchEvent")
                            {
                                await ExecuteTimerEventAsync(runId, branchNode, cancellationToken);
                            }
                            else
                            {
                                await _store.AppendEventAsync(
                                    runId,
                                    "node_completed",
                                    Serialize(new { runId, nodeId = branchNode.Id, nodeType = branchNode.ElementName }),
                                    cancellationToken);
                            }
                        }

                        var joinNode = definition.Nodes[joinIndex];
                        await _store.AppendEventAsync(
                            runId,
                            "parallel_joined",
                            Serialize(new { runId, gatewayId = joinNode.Id }),
                            cancellationToken);

                        await _store.AppendEventAsync(
                            runId,
                            "node_completed",
                            Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, nodeIndex = nextIndex }),
                            cancellationToken);

                        nextIndex = joinIndex + 1;
                        await SaveCheckpointAsync(runId, RunningStatus, nextIndex, waitingOnNodeId: null, completedAt: null, cancellationToken);
                        break;
                    }

                    await CompleteNodeAndCheckpointAsync(runId, node, nextIndex, cancellationToken);
                    nextIndex++;
                    break;
                case "intermediateCatchEvent":
                    await ExecuteTimerEventAsync(runId, node, cancellationToken);
                    nextIndex++;
                    await SaveCheckpointAsync(runId, RunningStatus, nextIndex, waitingOnNodeId: null, completedAt: null, cancellationToken);
                    break;
                case "boundaryEvent":
                    await _store.AppendEventAsync(
                        runId,
                        "boundary_event_registered",
                        Serialize(new { runId, boundaryNodeId = node.Id }),
                        cancellationToken);
                    await CompleteNodeAndCheckpointAsync(runId, node, nextIndex, cancellationToken);
                    nextIndex++;
                    break;
                case "endEvent":
                    await _store.AppendEventAsync(
                        runId,
                        "node_completed",
                        Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, nodeIndex = nextIndex }),
                        cancellationToken);

                    var completedAt = DateTime.UtcNow.ToString("o");
                    await _store.AppendEventAsync(
                        runId,
                        "run_completed",
                        Serialize(new { runId, completedAt }),
                        cancellationToken);

                    status = CompletedStatus;
                    nextIndex++;
                    await _store.UpdateRunStatusAsync(runId, status, completedAt, cancellationToken);
                    await SaveCheckpointAsync(runId, status, nextIndex, waitingOnNodeId: null, completedAt: completedAt, cancellationToken);

                    return new WorkflowExecutionState(
                        RunId: runId,
                        Status: status,
                        NextNodeIndex: nextIndex,
                        WaitingOnNodeId: null,
                        CompletedAt: completedAt);
            }
        }

        throw new InvalidOperationException("Workflow reached an invalid terminal state without an endEvent.");
    }

    private async Task SaveCheckpointAsync(
        string runId,
        string status,
        int nextNodeIndex,
        string? waitingOnNodeId,
        string? completedAt,
        CancellationToken cancellationToken)
    {
        await _store.AppendEventAsync(
            runId,
            "checkpoint_saved",
            Serialize(new CheckpointPayload(status, nextNodeIndex, waitingOnNodeId, completedAt)),
            cancellationToken);
    }

    private async Task<CheckpointPayload?> GetCheckpointAsync(string runId, CancellationToken cancellationToken)
    {
        var events = await _store.ListRunEventsAsync(runId, cancellationToken);

        var checkpointEvent = events
            .Where(static e => string.Equals(e.Type, "checkpoint_saved", StringComparison.Ordinal))
            .OrderBy(static e => e.CreatedAt)
            .LastOrDefault();

        if (checkpointEvent is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<CheckpointPayload>(checkpointEvent.Message, SerializerOptions);
    }

    private async Task ExecuteTimerEventAsync(
        string runId,
        BpmnNodeDefinition node,
        CancellationToken cancellationToken)
    {
        await _store.AppendEventAsync(
            runId,
            "timer_scheduled",
            Serialize(new { runId, nodeId = node.Id, timerType = "intermediateCatchEvent" }),
            cancellationToken);

        await _store.AppendEventAsync(
            runId,
            "timer_fired",
            Serialize(new { runId, nodeId = node.Id, firedAt = DateTime.UtcNow.ToString("o") }),
            cancellationToken);

        await _store.AppendEventAsync(
            runId,
            "node_completed",
            Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName }),
            cancellationToken);
    }

    private async Task CompleteNodeAndCheckpointAsync(
        string runId,
        BpmnNodeDefinition node,
        int nodeIndex,
        CancellationToken cancellationToken)
    {
        await _store.AppendEventAsync(
            runId,
            "node_completed",
            Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, nodeIndex }),
            cancellationToken);

        await SaveCheckpointAsync(runId, RunningStatus, nodeIndex + 1, waitingOnNodeId: null, completedAt: null, cancellationToken);
    }

    private async Task<ServiceExecutionResult> ExecuteServiceTaskAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        BpmnNodeDefinition node,
        int nodeIndex,
        CancellationToken cancellationToken)
    {
        var metadata = node.Metadata;
        var maxRetries = metadata?.MaxRetries ?? 0;
        var retryBackoffSeconds = metadata?.RetryBackoffSeconds ?? 0;
        var simulateTimeout = metadata?.SimulateTimeout ?? false;
        var timeoutSeconds = metadata?.TimeoutSeconds;
        var boundaryNode = FindBoundaryNodeAfter(definition, nodeIndex);

        var step = await _store.CreateStepAsync(
            runId,
            node.Id,
            node.Name,
            node.ElementName,
            metadata?.Agent,
            cancellationToken);

        if (simulateTimeout && boundaryNode is not null)
        {
            await _store.AppendEventAsync(runId, "timer_scheduled",
                Serialize(new { runId, nodeId = node.Id, timeoutSeconds = timeoutSeconds ?? 0 }), cancellationToken);
            await _store.AppendEventAsync(runId, "timeout_triggered",
                Serialize(new { runId, nodeId = node.Id, boundaryNodeId = boundaryNode.Id }), cancellationToken);
            await _store.AppendEventAsync(runId, "boundary_event_triggered",
                Serialize(new { runId, boundaryNodeId = boundaryNode.Id, sourceNodeId = node.Id }), cancellationToken);
            await _store.AppendEventAsync(runId, "node_completed",
                Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, nodeIndex, reason = "timeout_boundary" }), cancellationToken);

            await _store.UpdateStepStatusAsync(step.Id, "timed_out", output: null, DateTime.UtcNow.ToString("o"), cancellationToken);
            return ServiceExecutionResult.Completed;
        }

        var attempt = 1;
        while (true)
        {
            await _store.AppendEventAsync(
                runId,
                "service_task_attempted",
                Serialize(new
                {
                    runId,
                    nodeId = node.Id,
                    stepId = step.Id,
                    attempt,
                    maxRetries,
                    agent = metadata?.Agent,
                    action = metadata?.Action,
                    environment = metadata?.Environment,
                    purposeType = metadata?.PurposeType,
                    policyTag = metadata?.PolicyTag,
                    requiresEvidence = metadata?.RequiresEvidence
                }),
                cancellationToken);

            var outcome = await _serviceTaskExecutor.ExecuteAsync(runId, step.Id, node, attempt, cancellationToken);

            if (!outcome.Succeeded)
            {
                await _store.AppendEventAsync(runId, "service_task_failed",
                    Serialize(new { runId, nodeId = node.Id, stepId = step.Id, attempt, reason = outcome.FailureReason ?? "execution_error" }),
                    cancellationToken);

                if (attempt <= maxRetries)
                {
                    await _store.AppendEventAsync(runId, "retry_scheduled",
                        Serialize(new { runId, nodeId = node.Id, nextAttempt = attempt + 1, retryBackoffSeconds }),
                        cancellationToken);
                    attempt++;
                    continue;
                }

                if (boundaryNode is not null)
                {
                    await _store.AppendEventAsync(runId, "boundary_event_triggered",
                        Serialize(new { runId, boundaryNodeId = boundaryNode.Id, sourceNodeId = node.Id }), cancellationToken);
                    await _store.AppendEventAsync(runId, "node_completed",
                        Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, nodeIndex, reason = "retry_exhausted_boundary" }), cancellationToken);
                    await _store.UpdateStepStatusAsync(step.Id, FailedStatus, outcome.FailureReason, DateTime.UtcNow.ToString("o"), cancellationToken);
                    return ServiceExecutionResult.Completed;
                }

                await _store.AppendEventAsync(runId, "service_task_retry_exhausted",
                    Serialize(new { runId, nodeId = node.Id, stepId = step.Id, attempts = attempt }), cancellationToken);
                await _store.UpdateStepStatusAsync(step.Id, FailedStatus, outcome.FailureReason, DateTime.UtcNow.ToString("o"), cancellationToken);
                return ServiceExecutionResult.Failed;
            }

            await _store.AppendEventAsync(runId, "agent_output_recorded",
                Serialize(new { runId, nodeId = node.Id, stepId = step.Id, agent = metadata?.Agent, outputLength = outcome.Output?.Length ?? 0 }),
                cancellationToken);
            await _store.AppendEventAsync(runId, "node_completed",
                Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, nodeIndex }), cancellationToken);
            await _store.UpdateStepStatusAsync(step.Id, CompletedStatus, outcome.Output, DateTime.UtcNow.ToString("o"), cancellationToken);
            return ServiceExecutionResult.Completed;
        }
    }

    private static BpmnNodeDefinition? FindBoundaryNodeAfter(BpmnWorkflowDefinition definition, int nodeIndex)
    {
        var candidateIndex = nodeIndex + 1;
        if (candidateIndex < definition.Nodes.Count && definition.Nodes[candidateIndex].ElementName == "boundaryEvent")
        {
            return definition.Nodes[candidateIndex];
        }

        return null;
    }

    private static int FindParallelJoinIndex(BpmnWorkflowDefinition definition, int forkIndex)
    {
        for (var i = forkIndex + 1; i < definition.Nodes.Count; i++)
        {
            if (definition.Nodes[i].ElementName == "parallelGateway")
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSupportedRuntimeNode(string elementName)
    {
        return elementName is
            "startEvent" or
            "serviceTask" or
            "userTask" or
            "endEvent" or
            "exclusiveGateway" or
            "parallelGateway" or
            "intermediateCatchEvent" or
            "boundaryEvent";
    }

    private static void ValidateDefinition(BpmnWorkflowDefinition definition)
    {
        if (definition.Nodes.Count == 0)
        {
            throw new InvalidOperationException("Workflow definition must include at least one node.");
        }

        var hasStart = definition.Nodes.Any(static n => n.ElementName == "startEvent");
        var hasEnd = definition.Nodes.Any(static n => n.ElementName == "endEvent");

        if (!hasStart || !hasEnd)
        {
            throw new InvalidOperationException("Workflow definition must include both startEvent and endEvent nodes.");
        }

        if (definition.Nodes.Any(static n => (n.ElementName == "serviceTask" || n.ElementName == "scriptTask") && n.Metadata is null))
        {
            throw new InvalidOperationException("Service/script tasks must include parsed autofac:agentTask metadata.");
        }

        if (definition.Nodes.Any(static n => n.ElementName == "userTask" && n.ApprovalMetadata is null))
        {
            throw new InvalidOperationException("User tasks must include parsed autofac:approvalTask metadata.");
        }
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, SerializerOptions);
    }

    private sealed record CheckpointPayload(
        string Status,
        int NextNodeIndex,
        string? WaitingOnNodeId,
        string? CompletedAt);

    private enum ServiceExecutionResult
    {
        Completed,
        Failed
    }
}
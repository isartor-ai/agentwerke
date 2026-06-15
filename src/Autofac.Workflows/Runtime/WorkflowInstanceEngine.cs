using System.Text.Json;
using Autofac.Domain.Persistence;
using Autofac.Workflows.Bpmn;
using Microsoft.Extensions.Logging;

namespace Autofac.Workflows.Runtime;

public sealed class WorkflowInstanceEngine : IWorkflowEngineAdapter
{
    private const string RunningStatus = "running";
    private const string WaitingUserStatus = "waiting_user";
    private const string CompletedStatus = "completed";
    private const string FailedStatus = "failed";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkflowRuntimeStore _store;
    private readonly IServiceTaskExecutor _serviceTaskExecutor;
    private readonly ILogger<WorkflowInstanceEngine> _logger;

    public WorkflowInstanceEngine(
        IWorkflowRuntimeStore store,
        IServiceTaskExecutor serviceTaskExecutor,
        ILogger<WorkflowInstanceEngine> logger)
    {
        _store = store;
        _serviceTaskExecutor = serviceTaskExecutor;
        _logger = logger;
    }

    public string EngineId => "in-process";

    public Task<WorkflowExecutionState> StartAsync(
        string workflowDefinitionId,
        BpmnWorkflowDefinition definition,
        string? initiator,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        return StartAsync(
            new WorkflowEngineStartRequest(workflowDefinitionId, definition, initiator, correlationId),
            cancellationToken);
    }

    public Task<WorkflowExecutionState> ResumeAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        string? approvedBy,
        CancellationToken cancellationToken)
    {
        return ResumeAsync(
            new WorkflowEngineResumeRequest(runId, definition, approvedBy),
            cancellationToken);
    }

    public Task<WorkflowExecutionState> RecoverAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        CancellationToken cancellationToken)
    {
        return RecoverAsync(
            new WorkflowEngineRecoverRequest(runId, definition),
            cancellationToken);
    }

    public async Task<WorkflowExecutionState> StartAsync(
        WorkflowEngineStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = request.Definition;
        ValidateDefinition(definition);

        var run = await _store.CreateRunAsync(
            request.WorkflowDefinitionId, request.Initiator, cancellationToken, request.CorrelationId);

        _logger.LogInformation(
            "Workflow run started. RunId={RunId} WorkflowId={WorkflowId} Initiator={Initiator}",
            run.Id, request.WorkflowDefinitionId, request.Initiator);

        await _store.AppendEventAsync(run.Id, "run_started",
            Serialize(new { runId = run.Id, workflowDefinitionId = request.WorkflowDefinitionId, initiator = request.Initiator, correlationId = request.CorrelationId }),
            cancellationToken);

        return await AdvanceAsync(run.Id, definition, startNodeId: null, cancellationToken);
    }

    public async Task<WorkflowExecutionState> ResumeAsync(
        WorkflowEngineResumeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateDefinition(request.Definition);

        var checkpoint = await GetCheckpointAsync(request.RunId, cancellationToken)
            ?? throw new InvalidOperationException($"No persisted checkpoint exists for run '{request.RunId}'.");

        if (!string.Equals(checkpoint.Status, WaitingUserStatus, StringComparison.Ordinal))
            throw new InvalidOperationException($"Run '{request.RunId}' is not waiting for user input.");

        await _store.AppendEventAsync(request.RunId, "user_task_completed",
            Serialize(new
            {
                runId = request.RunId,
                nodeId = checkpoint.WaitingOnNodeId,
                approvedBy = request.ApprovedBy,
                timestampUtc = DateTime.UtcNow.ToString("o")
            }),
            cancellationToken);

        return await AdvanceAsync(request.RunId, request.Definition, checkpoint.NextNodeId, cancellationToken);
    }

    public async Task<WorkflowExecutionState> RecoverAsync(
        WorkflowEngineRecoverRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateDefinition(request.Definition);

        _ = await _store.GetRunAsync(request.RunId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow run '{request.RunId}' was not found.");

        var checkpoint = await GetCheckpointAsync(request.RunId, cancellationToken)
            ?? throw new InvalidOperationException($"No persisted checkpoint exists for run '{request.RunId}'.");

        if (string.Equals(checkpoint.Status, CompletedStatus, StringComparison.Ordinal))
        {
            return new WorkflowExecutionState(
                RunId: request.RunId, Status: CompletedStatus,
                NextNodeId: null, WaitingOnNodeId: null, CompletedAt: checkpoint.CompletedAt);
        }

        if (string.Equals(checkpoint.Status, WaitingUserStatus, StringComparison.Ordinal))
        {
            return new WorkflowExecutionState(
                RunId: request.RunId, Status: WaitingUserStatus,
                NextNodeId: checkpoint.NextNodeId, WaitingOnNodeId: checkpoint.WaitingOnNodeId,
                CompletedAt: null);
        }

        return await AdvanceAsync(request.RunId, request.Definition, checkpoint.NextNodeId, cancellationToken);
    }

    // ── Graph execution ───────────────────────────────────────────────────────

    private async Task<WorkflowExecutionState> AdvanceAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        string? startNodeId,
        CancellationToken cancellationToken)
    {
        var graph = FlowGraph.Build(definition);
        var currentNodeId = startNodeId ?? definition.Nodes[0].Id;

        await _store.UpdateRunStatusAsync(runId, RunningStatus, completedAt: null, cancellationToken);

        while (true)
        {
            if (!graph.NodeById.TryGetValue(currentNodeId, out var node))
                throw new InvalidOperationException($"Node '{currentNodeId}' not found in workflow definition.");

            if (!IsSupportedRuntimeNode(node.ElementName))
                throw new InvalidOperationException($"Node '{node.Id}' type '{node.ElementName}' is not supported by the in-process engine.");

            await _store.AppendEventAsync(runId, "node_entered",
                Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName }),
                cancellationToken);

            switch (node.ElementName)
            {
                case "startEvent":
                    await CompleteNodeAsync(runId, node, cancellationToken);
                    await SaveCheckpointAsync(runId, RunningStatus, graph.GetSingleSuccessor(node.Id), null, null, cancellationToken);
                    currentNodeId = graph.GetSingleSuccessor(node.Id);
                    break;

                case "serviceTask":
                    var result = await ExecuteServiceTaskAsync(runId, definition, graph, node, cancellationToken);
                    if (result == ServiceExecutionResult.Failed)
                    {
                        await _store.UpdateRunStatusAsync(runId, FailedStatus, completedAt: null, cancellationToken);
                        await SaveCheckpointAsync(runId, FailedStatus, null, null, null, cancellationToken);
                        return new WorkflowExecutionState(runId, FailedStatus, null, null, null);
                    }
                    var afterService = graph.GetSingleSuccessor(node.Id);
                    // Skip the boundary event node when present — it was already handled inline
                    if (graph.NodeById.TryGetValue(afterService, out var afterNode) && afterNode.ElementName == "boundaryEvent")
                        afterService = graph.GetSingleSuccessor(afterService);
                    await SaveCheckpointAsync(runId, RunningStatus, afterService, null, null, cancellationToken);
                    currentNodeId = afterService;
                    break;

                case "userTask":
                    var nextAfterUser = graph.GetSingleSuccessor(node.Id);
                    await _store.AppendEventAsync(runId, "user_task_waiting",
                        Serialize(new
                        {
                            runId, nodeId = node.Id,
                            purposeType = node.ApprovalMetadata?.PurposeType,
                            policyTag = node.ApprovalMetadata?.PolicyTag
                        }), cancellationToken);
                    await _store.UpdateRunStatusAsync(runId, WaitingUserStatus, completedAt: null, cancellationToken);
                    await SaveCheckpointAsync(runId, WaitingUserStatus, nextAfterUser, node.Id, null, cancellationToken);
                    return new WorkflowExecutionState(runId, WaitingUserStatus, nextAfterUser, node.Id, null);

                case "exclusiveGateway":
                    await _store.AppendEventAsync(runId, "gateway_evaluated",
                        Serialize(new { runId, gatewayId = node.Id, gatewayType = "exclusive" }),
                        cancellationToken);
                    await CompleteNodeAsync(runId, node, cancellationToken);
                    var exclusiveNext = graph.GetConditionalSuccessor(node.Id);
                    await SaveCheckpointAsync(runId, RunningStatus, exclusiveNext, null, null, cancellationToken);
                    currentNodeId = exclusiveNext;
                    break;

                case "parallelGateway":
                    var outgoing = graph.GetOutgoing(node.Id);
                    if (outgoing.Count > 1)
                    {
                        // FORK
                        var branchNodeIds = outgoing.Select(static f => f.TargetRef).ToList();
                        var joinNodeId = graph.FindJoin(branchNodeIds);

                        await _store.AppendEventAsync(runId, "parallel_forked",
                            Serialize(new { runId, gatewayId = node.Id, branchNodeIds }),
                            cancellationToken);

                        foreach (var branchId in branchNodeIds)
                        {
                            var branchResult = await ExecuteBranchAsync(runId, definition, graph, branchId, joinNodeId, cancellationToken);
                            if (branchResult == ServiceExecutionResult.Failed)
                            {
                                await _store.UpdateRunStatusAsync(runId, FailedStatus, completedAt: null, cancellationToken);
                                await SaveCheckpointAsync(runId, FailedStatus, null, null, null, cancellationToken);
                                return new WorkflowExecutionState(runId, FailedStatus, null, null, null);
                            }
                        }

                        var afterJoin = graph.GetSingleSuccessor(joinNodeId);
                        await _store.AppendEventAsync(runId, "parallel_joined",
                            Serialize(new { runId, gatewayId = joinNodeId }),
                            cancellationToken);
                        await CompleteNodeAsync(runId, node, cancellationToken);
                        await SaveCheckpointAsync(runId, RunningStatus, afterJoin, null, null, cancellationToken);
                        currentNodeId = afterJoin;
                    }
                    else
                    {
                        // JOIN (reached after all branches complete) — should not be hit in normal flow
                        await CompleteNodeAsync(runId, node, cancellationToken);
                        currentNodeId = graph.GetSingleSuccessor(node.Id);
                    }
                    break;

                case "intermediateCatchEvent":
                    // Timer fires immediately in in-process mode (Phase C adds real scheduling)
                    await _store.AppendEventAsync(runId, "timer_fired",
                        Serialize(new { runId, nodeId = node.Id, firedAt = DateTime.UtcNow.ToString("o") }),
                        cancellationToken);
                    await CompleteNodeAsync(runId, node, cancellationToken);
                    currentNodeId = graph.GetSingleSuccessor(node.Id);
                    break;

                case "boundaryEvent":
                    // Handled inline by service task execution; if reached directly, just pass through
                    await _store.AppendEventAsync(runId, "boundary_event_registered",
                        Serialize(new { runId, boundaryNodeId = node.Id }),
                        cancellationToken);
                    await CompleteNodeAsync(runId, node, cancellationToken);
                    currentNodeId = graph.GetSingleSuccessor(node.Id);
                    break;

                case "endEvent":
                    var completedAt = DateTime.UtcNow.ToString("o");
                    await CompleteNodeAsync(runId, node, cancellationToken);
                    await _store.AppendEventAsync(runId, "run_completed",
                        Serialize(new { runId, completedAt }), cancellationToken);
                    _logger.LogInformation("Workflow run completed. RunId={RunId}", runId);
                    await _store.UpdateRunStatusAsync(runId, CompletedStatus, completedAt, cancellationToken);
                    await SaveCheckpointAsync(runId, CompletedStatus, null, null, completedAt, cancellationToken);
                    return new WorkflowExecutionState(runId, CompletedStatus, null, null, completedAt);
            }
        }
    }

    private async Task<ServiceExecutionResult> ExecuteBranchAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        FlowGraph graph,
        string branchStartId,
        string joinNodeId,
        CancellationToken cancellationToken)
    {
        var nodeId = branchStartId;
        while (!string.Equals(nodeId, joinNodeId, StringComparison.Ordinal))
        {
            if (!graph.NodeById.TryGetValue(nodeId, out var branchNode))
                throw new InvalidOperationException($"Branch node '{nodeId}' not found.");

            await _store.AppendEventAsync(runId, "parallel_branch_entered",
                Serialize(new { runId, branchNodeId = branchNode.Id, branchNodeType = branchNode.ElementName }),
                cancellationToken);

            if (branchNode.ElementName == "serviceTask")
            {
                var result = await ExecuteServiceTaskAsync(runId, definition, graph, branchNode, cancellationToken);
                if (result == ServiceExecutionResult.Failed)
                    return ServiceExecutionResult.Failed;
            }
            else
            {
                await _store.AppendEventAsync(runId, "node_completed",
                    Serialize(new { runId, nodeId = branchNode.Id, nodeType = branchNode.ElementName }),
                    cancellationToken);
            }

            nodeId = graph.GetSingleSuccessor(branchNode.Id);
        }
        return ServiceExecutionResult.Completed;
    }

    private async Task<ServiceExecutionResult> ExecuteServiceTaskAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        FlowGraph graph,
        BpmnNodeDefinition node,
        CancellationToken cancellationToken)
    {
        var metadata = node.Metadata;
        var maxRetries = metadata?.MaxRetries ?? 0;
        var retryBackoffSeconds = metadata?.RetryBackoffSeconds ?? 0;
        var simulateTimeout = metadata?.SimulateTimeout ?? false;
        var timeoutSeconds = metadata?.TimeoutSeconds;

        // Find boundary event (adjacent node in flow that is a boundaryEvent)
        var successorId = graph.GetSingleSuccessorOrNull(node.Id);
        BpmnNodeDefinition? boundaryNode = null;
        if (successorId is not null
            && graph.NodeById.TryGetValue(successorId, out var candidate)
            && candidate.ElementName == "boundaryEvent")
        {
            boundaryNode = candidate;
        }

        var step = await _store.CreateStepAsync(
            runId, node.Id, node.Name, node.ElementName, metadata?.Agent, cancellationToken);

        if (simulateTimeout && boundaryNode is not null)
        {
            await _store.AppendEventAsync(runId, "timer_scheduled",
                Serialize(new { runId, nodeId = node.Id, timeoutSeconds = timeoutSeconds ?? 0 }),
                cancellationToken);
            await _store.AppendEventAsync(runId, "timeout_triggered",
                Serialize(new { runId, nodeId = node.Id, boundaryNodeId = boundaryNode.Id }),
                cancellationToken);
            await _store.AppendEventAsync(runId, "boundary_event_triggered",
                Serialize(new { runId, boundaryNodeId = boundaryNode.Id, sourceNodeId = node.Id }),
                cancellationToken);
            await _store.AppendEventAsync(runId, "node_completed",
                Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, reason = "timeout_boundary" }),
                cancellationToken);
            await _store.UpdateStepStatusAsync(step.Id, "timed_out", null, null, DateTime.UtcNow.ToString("o"), null, null, cancellationToken);
            return ServiceExecutionResult.Completed;
        }

        var attempt = 1;
        while (true)
        {
            await _store.AppendEventAsync(runId, "service_task_attempted",
                Serialize(new
                {
                    runId, nodeId = node.Id, stepId = step.Id, attempt, maxRetries,
                    agent = metadata?.Agent, action = metadata?.Action,
                    environment = metadata?.Environment,
                    purposeType = metadata?.PurposeType, policyTag = metadata?.PolicyTag,
                    requiresEvidence = metadata?.RequiresEvidence
                }), cancellationToken);

            var outcome = await _serviceTaskExecutor.ExecuteAsync(runId, step.Id, node, attempt, cancellationToken);

            if (outcome.PolicyDecision is not null)
            {
                await _store.AppendEventAsync(runId, "policy_decision_recorded",
                    Serialize(new
                    {
                        runId, nodeId = node.Id, stepId = step.Id,
                        kind = outcome.PolicyDecision.Kind,
                        policyId = outcome.PolicyDecision.PolicyId,
                        policyName = outcome.PolicyDecision.PolicyName,
                        rationale = outcome.PolicyDecision.Rationale,
                        riskScore = outcome.PolicyDecision.RiskScore,
                        riskLevel = outcome.PolicyDecision.RiskLevel,
                        constraints = outcome.PolicyDecision.Constraints
                    }), cancellationToken);
            }

            foreach (var action in outcome.ExternalActions ?? [])
            {
                await _store.AppendEventAsync(runId, "external_action_recorded",
                    Serialize(new
                    {
                        runId, nodeId = node.Id, stepId = step.Id,
                        provider = action.Provider, action = action.Action,
                        status = action.Status, resourceId = action.ResourceId,
                        resourceUrl = action.ResourceUrl, summary = action.Summary, attempt
                    }), cancellationToken);
            }

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
                    if (retryBackoffSeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(retryBackoffSeconds), cancellationToken);
                    attempt++;
                    continue;
                }

                if (boundaryNode is not null)
                {
                    await _store.AppendEventAsync(runId, "boundary_event_triggered",
                        Serialize(new { runId, boundaryNodeId = boundaryNode.Id, sourceNodeId = node.Id }),
                        cancellationToken);
                    await _store.AppendEventAsync(runId, "node_completed",
                        Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, reason = "retry_exhausted_boundary" }),
                        cancellationToken);
                    await _store.UpdateStepStatusAsync(step.Id, FailedStatus, null, outcome.FailureReason, DateTime.UtcNow.ToString("o"), outcome.PolicyDecision, outcome.RuntimeSnapshot, cancellationToken);
                    return ServiceExecutionResult.Completed;
                }

                await _store.AppendEventAsync(runId, "service_task_retry_exhausted",
                    Serialize(new { runId, nodeId = node.Id, stepId = step.Id, attempts = attempt }),
                    cancellationToken);
                await _store.UpdateStepStatusAsync(step.Id, FailedStatus, null, outcome.FailureReason, DateTime.UtcNow.ToString("o"), outcome.PolicyDecision, outcome.RuntimeSnapshot, cancellationToken);
                return ServiceExecutionResult.Failed;
            }

            await _store.AppendEventAsync(runId, "agent_output_recorded",
                Serialize(new { runId, nodeId = node.Id, stepId = step.Id, agent = metadata?.Agent, outputLength = outcome.Output?.Length ?? 0 }),
                cancellationToken);
            await _store.AppendEventAsync(runId, "node_completed",
                Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName }),
                cancellationToken);
            await _store.UpdateStepStatusAsync(step.Id, CompletedStatus, outcome.Output, null, DateTime.UtcNow.ToString("o"), outcome.PolicyDecision, outcome.RuntimeSnapshot, cancellationToken);
            return ServiceExecutionResult.Completed;
        }
    }

    // ── Checkpoint helpers ────────────────────────────────────────────────────

    private async Task SaveCheckpointAsync(
        string runId,
        string status,
        string? nextNodeId,
        string? waitingOnNodeId,
        string? completedAt,
        CancellationToken cancellationToken)
    {
        await _store.AppendEventAsync(runId, "checkpoint_saved",
            Serialize(new CheckpointPayload(status, nextNodeId, waitingOnNodeId, completedAt)),
            cancellationToken);
    }

    private async Task<CheckpointPayload?> GetCheckpointAsync(string runId, CancellationToken cancellationToken)
    {
        var events = await _store.ListRunEventsAsync(runId, cancellationToken);
        var last = events
            .Where(static e => string.Equals(e.Type, "checkpoint_saved", StringComparison.Ordinal))
            .OrderBy(static e => e.CreatedAt)
            .LastOrDefault();

        return last is null ? null : JsonSerializer.Deserialize<CheckpointPayload>(last.Message, SerializerOptions);
    }

    private async Task CompleteNodeAsync(string runId, BpmnNodeDefinition node, CancellationToken cancellationToken)
    {
        await _store.AppendEventAsync(runId, "node_completed",
            Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName }),
            cancellationToken);
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static bool IsSupportedRuntimeNode(string elementName) =>
        elementName is "startEvent" or "serviceTask" or "userTask" or "endEvent" or
                       "exclusiveGateway" or "parallelGateway" or "intermediateCatchEvent" or "boundaryEvent";

    private static void ValidateDefinition(BpmnWorkflowDefinition definition)
    {
        if (definition.Nodes.Count == 0)
            throw new InvalidOperationException("Workflow definition must include at least one node.");

        var hasStart = definition.Nodes.Any(static n => n.ElementName == "startEvent");
        var hasEnd = definition.Nodes.Any(static n => n.ElementName == "endEvent");

        if (!hasStart || !hasEnd)
            throw new InvalidOperationException("Workflow definition must include both startEvent and endEvent nodes.");

        if (definition.Nodes.Any(static n => (n.ElementName is "serviceTask" or "scriptTask") && n.Metadata is null))
            throw new InvalidOperationException("Service/script tasks must include parsed autofac:agentTask metadata.");

        if (definition.Nodes.Any(static n => n.ElementName == "userTask" && n.ApprovalMetadata is null))
            throw new InvalidOperationException("User tasks must include parsed autofac:approvalTask metadata.");
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);

    private sealed record CheckpointPayload(
        string Status,
        string? NextNodeId,
        string? WaitingOnNodeId,
        string? CompletedAt);

    private enum ServiceExecutionResult { Completed, Failed }

    // ── Flow graph ────────────────────────────────────────────────────────────

    private sealed class FlowGraph
    {
        public IReadOnlyDictionary<string, BpmnNodeDefinition> NodeById { get; }
        private readonly IReadOnlyDictionary<string, IReadOnlyList<BpmnSequenceFlow>> _outgoing;

        private FlowGraph(
            IReadOnlyDictionary<string, BpmnNodeDefinition> nodeById,
            IReadOnlyDictionary<string, IReadOnlyList<BpmnSequenceFlow>> outgoing)
        {
            NodeById = nodeById;
            _outgoing = outgoing;
        }

        public static FlowGraph Build(BpmnWorkflowDefinition definition)
        {
            var nodeById = definition.Nodes.ToDictionary(static n => n.Id, StringComparer.Ordinal);

            IReadOnlyDictionary<string, IReadOnlyList<BpmnSequenceFlow>> outgoing;

            if (definition.SequenceFlows is { Count: > 0 })
            {
                outgoing = definition.SequenceFlows
                    .GroupBy(static f => f.SourceRef, StringComparer.Ordinal)
                    .ToDictionary(
                        static g => g.Key,
                        static g => (IReadOnlyList<BpmnSequenceFlow>)g.ToList(),
                        StringComparer.Ordinal);
            }
            else
            {
                outgoing = InferFlows(definition.Nodes);
            }

            return new FlowGraph(nodeById, outgoing);
        }

        public IReadOnlyList<BpmnSequenceFlow> GetOutgoing(string nodeId) =>
            _outgoing.TryGetValue(nodeId, out var flows) ? flows : [];

        public string GetSingleSuccessor(string nodeId)
        {
            var flows = GetOutgoing(nodeId);
            return flows.Count > 0
                ? flows[0].TargetRef
                : throw new InvalidOperationException($"Node '{nodeId}' has no outgoing sequence flow.");
        }

        public string? GetSingleSuccessorOrNull(string nodeId)
        {
            var flows = GetOutgoing(nodeId);
            return flows.Count > 0 ? flows[0].TargetRef : null;
        }

        public string GetConditionalSuccessor(string nodeId)
        {
            var flows = GetOutgoing(nodeId);
            if (flows.Count == 0)
                throw new InvalidOperationException($"Exclusive gateway '{nodeId}' has no outgoing sequence flows.");

            // Prefer a flow whose condition evaluates to true; fall back to first unconditional flow
            foreach (var flow in flows)
            {
                if (EvaluateCondition(flow.ConditionExpression))
                    return flow.TargetRef;
            }

            // Return the first flow with no condition as the default path
            var defaultFlow = flows.FirstOrDefault(static f => f.ConditionExpression is null)
                ?? flows[0];
            return defaultFlow.TargetRef;
        }

        public string FindJoin(IReadOnlyList<string> branchStartIds)
        {
            // Trace each branch forward until they converge on the same node (a parallelGateway join)
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var startId in branchStartIds)
            {
                var nodeId = startId;
                while (nodeId is not null)
                {
                    if (NodeById.TryGetValue(nodeId, out var n) && n.ElementName == "parallelGateway")
                    {
                        if (!visited.Add(nodeId))
                            return nodeId; // second branch reached same join
                        break;
                    }
                    nodeId = GetSingleSuccessorOrNull(nodeId);
                }
            }

            // Single-branch path or fallback: find the next parallelGateway reachable from branchStartIds[0]
            var candidate = branchStartIds[0];
            while (candidate is not null)
            {
                if (NodeById.TryGetValue(candidate, out var cn) && cn.ElementName == "parallelGateway")
                    return candidate;
                candidate = GetSingleSuccessorOrNull(candidate)!;
            }

            throw new InvalidOperationException("Could not locate parallel join gateway.");
        }

        private static bool EvaluateCondition(string? expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return false;
            var expr = expression.Trim();
            if (expr is "true" or "${true}" or "yes") return true;
            if (expr is "false" or "${false}" or "no") return false;
            // Unknown condition — treat as truthy (matches first defined path)
            return true;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<BpmnSequenceFlow>> InferFlows(
            IReadOnlyList<BpmnNodeDefinition> nodes)
        {
            var result = new Dictionary<string, List<BpmnSequenceFlow>>(StringComparer.Ordinal);

            var i = 0;
            while (i < nodes.Count - 1)
            {
                var node = nodes[i];

                if (node.ElementName == "parallelGateway")
                {
                    var joinIdx = FindParallelJoinIndex(nodes, i);
                    if (joinIdx > i)
                    {
                        // Branches: fork → each node between fork and join
                        for (var b = i + 1; b < joinIdx; b++)
                        {
                            AddFlow(result, node.Id, nodes[b].Id);
                            AddFlow(result, nodes[b].Id, nodes[joinIdx].Id);
                        }
                        // After join
                        if (joinIdx + 1 < nodes.Count)
                            AddFlow(result, nodes[joinIdx].Id, nodes[joinIdx + 1].Id);
                        i = joinIdx + 1;
                        continue;
                    }
                }

                AddFlow(result, node.Id, nodes[i + 1].Id);
                i++;
            }

            return result.ToDictionary(
                static kv => kv.Key,
                static kv => (IReadOnlyList<BpmnSequenceFlow>)kv.Value.AsReadOnly(),
                StringComparer.Ordinal);
        }

        private static void AddFlow(
            Dictionary<string, List<BpmnSequenceFlow>> dict, string src, string tgt)
        {
            if (!dict.TryGetValue(src, out var list)) { list = []; dict[src] = list; }
            list.Add(new BpmnSequenceFlow($"inf_{src}_{tgt}", src, tgt, null));
        }

        private static int FindParallelJoinIndex(IReadOnlyList<BpmnNodeDefinition> nodes, int forkIndex)
        {
            for (var k = forkIndex + 1; k < nodes.Count; k++)
            {
                if (nodes[k].ElementName == "parallelGateway")
                    return k;
            }
            return -1;
        }
    }
}

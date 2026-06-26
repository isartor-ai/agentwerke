using System.Text.Json;
using System.Text.RegularExpressions;
using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Autofac.Workflows.Bpmn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Autofac.Workflows.Runtime;

public sealed class WorkflowInstanceEngine : IWorkflowEngineAdapter
{
    private const string RunningStatus = "running";
    private const string WaitingUserStatus = "waiting_user";
    private const string WaitingTimerStatus = "waiting_timer";
    private const string WaitingExternalStatus = "waiting_external";
    private const string CompletedStatus = "completed";
    private const string FailedStatus = "failed";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex TemplateVariablePattern = new("{{\\s*([a-zA-Z0-9_.-]+)\\s*}}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IWorkflowRuntimeStore _store;
    private readonly IServiceTaskExecutor _serviceTaskExecutor;
    private readonly IRunContextRepository _runContext;
    private readonly ILogger<WorkflowInstanceEngine> _logger;
    private readonly IServiceScopeFactory? _serviceScopeFactory;

    public WorkflowInstanceEngine(
        IWorkflowRuntimeStore store,
        IServiceTaskExecutor serviceTaskExecutor,
        IRunContextRepository runContext,
        ILogger<WorkflowInstanceEngine> logger,
        IServiceScopeFactory? serviceScopeFactory = null)
    {
        _store = store;
        _serviceTaskExecutor = serviceTaskExecutor;
        _runContext = runContext;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
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

        var workflowDefinitionId = request.WorkflowDefinitionId;
        var definition = request.Definition;
        var initiator = request.Initiator;
        var correlationId = request.CorrelationId;
        ValidateDefinition(definition);

        WorkflowRun run;
        if (request.ExistingRunId is not null)
        {
            run = await _store.GetRunAsync(request.ExistingRunId, cancellationToken)
                ?? throw new InvalidOperationException($"Pre-created run '{request.ExistingRunId}' was not found.");
        }
        else
        {
            run = await _store.CreateRunAsync(
                workflowDefinitionId,
                initiator,
                cancellationToken,
                correlationId);
        }

        _logger.LogInformation(
            "Workflow run started. RunId={RunId} WorkflowId={WorkflowId} Initiator={Initiator} CorrelationId={CorrelationId}",
            run.Id, workflowDefinitionId, initiator, correlationId);

        await _store.AppendEventAsync(run.Id, "run_started",
            Serialize(new { runId = run.Id, workflowDefinitionId, initiator, correlationId }),
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

        if (string.Equals(checkpoint.Status, WaitingUserStatus, StringComparison.Ordinal))
        {
            await _store.AppendEventAsync(request.RunId, "user_task_completed",
                Serialize(new
                {
                    runId = request.RunId,
                    nodeId = checkpoint.WaitingOnNodeId,
                    approvedBy = request.ApprovedBy,
                    timestampUtc = DateTime.UtcNow.ToString("o")
                }),
                cancellationToken);
        }
        else if (string.Equals(checkpoint.Status, WaitingExternalStatus, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(request.ExternalCorrelationKey))
            {
                throw new InvalidOperationException($"Run '{request.RunId}' requires an external correlation key to resume.");
            }

            if (!string.Equals(checkpoint.ExternalCorrelationKey, request.ExternalCorrelationKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Run '{request.RunId}' correlation key '{request.ExternalCorrelationKey}' does not match the waiting checkpoint.");
            }

            await MergeExternalPayloadAsync(request.RunId, request.ExternalPayload ?? new Dictionary<string, string>(), cancellationToken);

            await _store.AppendEventAsync(request.RunId, "external_event_received",
                Serialize(new
                {
                    runId = request.RunId,
                    nodeId = checkpoint.WaitingOnNodeId,
                    messageName = checkpoint.ExternalMessageName,
                    correlationKey = request.ExternalCorrelationKey,
                    payload = request.ExternalPayload ?? new Dictionary<string, string>(),
                    resumedBy = request.ResumedBy,
                    timestampUtc = DateTime.UtcNow.ToString("o")
                }),
                cancellationToken);
        }
        else
        {
            throw new InvalidOperationException($"Run '{request.RunId}' is not waiting for resumable input.");
        }

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
            var artifactName = checkpoint.WaitingOnNodeId is null
                ? null
                : await FindWaitingApprovalArtifactNameAsync(request.RunId, request.Definition, checkpoint.WaitingOnNodeId, cancellationToken);
            return new WorkflowExecutionState(
                RunId: request.RunId, Status: WaitingUserStatus,
                NextNodeId: checkpoint.NextNodeId, WaitingOnNodeId: checkpoint.WaitingOnNodeId,
                CompletedAt: null, WaitingApprovalArtifactName: artifactName);
        }

        if (string.Equals(checkpoint.Status, WaitingTimerStatus, StringComparison.Ordinal))
        {
            await _store.AppendEventAsync(
                request.RunId,
                "timer_fired",
                Serialize(new { runId = request.RunId, nodeId = checkpoint.WaitingOnNodeId, firedAt = DateTime.UtcNow.ToString("o") }),
            cancellationToken);
        }

        if (string.Equals(checkpoint.Status, WaitingExternalStatus, StringComparison.Ordinal))
        {
            return new WorkflowExecutionState(
                RunId: request.RunId,
                Status: WaitingExternalStatus,
                NextNodeId: checkpoint.NextNodeId,
                WaitingOnNodeId: checkpoint.WaitingOnNodeId,
                CompletedAt: null,
                WaitingExternalCorrelationKey: checkpoint.ExternalCorrelationKey,
                WaitingExternalMessageName: checkpoint.ExternalMessageName);
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
                    // Intentionally do NOT flip the run's Status to waiting_user here. The
                    // executor (WorkflowRunExecutor.HandleResultAsync) creates the approval
                    // row and THEN sets the status, so the run never appears as
                    // awaiting_approval before its approval is queryable (#163). The
                    // checkpoint still records waiting_user for crash recovery.
                    await SaveCheckpointAsync(runId, WaitingUserStatus, nextAfterUser, node.Id, null, cancellationToken);
                    var artifactName = await FindWaitingApprovalArtifactNameAsync(runId, definition, node.Id, cancellationToken);
                    return new WorkflowExecutionState(runId, WaitingUserStatus, nextAfterUser, node.Id, null, WaitingApprovalArtifactName: artifactName);

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

                        ServiceExecutionResult[] branchResults;

                        if (_serviceScopeFactory is not null)
                        {
                            branchResults = await Task.WhenAll(
                                branchNodeIds.Select(branchId =>
                                    ExecuteBranchInScopeAsync(runId, definition, graph, branchId, joinNodeId, cancellationToken)));
                        }
                        else
                        {
                            var results = new List<ServiceExecutionResult>(branchNodeIds.Count);
                            foreach (var branchId in branchNodeIds)
                            {
                                results.Add(await ExecuteBranchAsync(runId, definition, graph, branchId, joinNodeId, cancellationToken));
                            }

                            branchResults = results.ToArray();
                        }

                        if (branchResults.Any(static result => result == ServiceExecutionResult.Failed))
                        {
                            await _store.UpdateRunStatusAsync(runId, FailedStatus, completedAt: null, cancellationToken);
                            await SaveCheckpointAsync(runId, FailedStatus, null, null, null, cancellationToken);
                            return new WorkflowExecutionState(runId, FailedStatus, null, null, null);
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
                    if (node.ExternalEventMetadata is not null)
                    {
                        return await WaitForExternalEventAsync(
                            runId,
                            node,
                            graph.GetSingleSuccessor(node.Id),
                            cancellationToken);
                    }

                    return await ScheduleTimerAndPauseAsync(
                        runId,
                        node,
                        graph.GetSingleSuccessor(node.Id),
                        cancellationToken);

                case "receiveTask":
                    return await WaitForExternalEventAsync(
                        runId,
                        node,
                        graph.GetSingleSuccessor(node.Id),
                        cancellationToken);

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
            else if (branchNode.ElementName == "intermediateCatchEvent")
            {
                await ExecuteParallelTimerAsync(runId, branchNode, cancellationToken);
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

    private async Task<ServiceExecutionResult> ExecuteBranchInScopeAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        FlowGraph graph,
        string branchStartId,
        string joinNodeId,
        CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory!.CreateAsyncScope();
        var scopedStore = scope.ServiceProvider.GetRequiredService<IWorkflowRuntimeStore>();
        var scopedExecutor = scope.ServiceProvider.GetRequiredService<IServiceTaskExecutor>();
        var scopedRunContext = scope.ServiceProvider.GetRequiredService<IRunContextRepository>();

        var nodeId = branchStartId;
        while (!string.Equals(nodeId, joinNodeId, StringComparison.Ordinal))
        {
            if (!graph.NodeById.TryGetValue(nodeId, out var branchNode))
                throw new InvalidOperationException($"Branch node '{nodeId}' not found.");

            await scopedStore.AppendEventAsync(runId, "parallel_branch_entered",
                Serialize(new { runId, branchNodeId = branchNode.Id, branchNodeType = branchNode.ElementName }),
                cancellationToken);

            if (branchNode.ElementName == "serviceTask")
            {
                var result = await ExecuteServiceTaskAsync(
                    runId,
                    definition,
                    graph,
                    branchNode,
                    cancellationToken,
                    scopedStore,
                    scopedExecutor,
                    scopedRunContext);

                if (result == ServiceExecutionResult.Failed)
                    return ServiceExecutionResult.Failed;
            }
            else if (branchNode.ElementName == "intermediateCatchEvent")
            {
                await ExecuteParallelTimerAsync(runId, branchNode, cancellationToken, scopedStore);
            }
            else
            {
                await scopedStore.AppendEventAsync(runId, "node_completed",
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
        CancellationToken cancellationToken,
        IWorkflowRuntimeStore? storeOverride = null,
        IServiceTaskExecutor? executorOverride = null,
        IRunContextRepository? runContextOverride = null)
    {
        var store = storeOverride ?? _store;
        var executor = executorOverride ?? _serviceTaskExecutor;
        var runContext = runContextOverride ?? _runContext;
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

        var step = await store.CreateStepAsync(
            runId, node.Id, node.Name, node.ElementName, metadata?.Agent, cancellationToken);

        if (simulateTimeout && boundaryNode is not null)
        {
            await store.AppendEventAsync(runId, "timer_scheduled",
                Serialize(new { runId, nodeId = node.Id, timeoutSeconds = timeoutSeconds ?? 0 }),
                cancellationToken);
            await store.AppendEventAsync(runId, "timeout_triggered",
                Serialize(new { runId, nodeId = node.Id, boundaryNodeId = boundaryNode.Id }),
                cancellationToken);
            await store.AppendEventAsync(runId, "boundary_event_triggered",
                Serialize(new { runId, boundaryNodeId = boundaryNode.Id, sourceNodeId = node.Id }),
                cancellationToken);
            await store.AppendEventAsync(runId, "node_completed",
                Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, reason = "timeout_boundary" }),
                cancellationToken);
            await store.UpdateStepStatusAsync(step.Id, "timed_out", null, null, DateTime.UtcNow.ToString("o"), null, null, cancellationToken);
            return ServiceExecutionResult.Completed;
        }

        var attempt = 1;
        while (true)
        {
            await store.AppendEventAsync(runId, "service_task_attempted",
                Serialize(new
                {
                    runId, nodeId = node.Id, stepId = step.Id, attempt, maxRetries,
                    agent = metadata?.Agent, action = metadata?.Action,
                    environment = metadata?.Environment,
                    purposeType = metadata?.PurposeType, policyTag = metadata?.PolicyTag,
                    requiresEvidence = metadata?.RequiresEvidence
                }), cancellationToken);

            var outcome = await executor.ExecuteAsync(runId, step.Id, node, attempt, cancellationToken);

            if (outcome.PolicyDecision is not null)
            {
                await store.AppendEventAsync(runId, "policy_decision_recorded",
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
                await store.AppendEventAsync(runId, "external_action_recorded",
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
                await store.AppendEventAsync(runId, "service_task_failed",
                    Serialize(new { runId, nodeId = node.Id, stepId = step.Id, attempt, reason = outcome.FailureReason ?? "execution_error" }),
                    cancellationToken);

                if (attempt <= maxRetries)
                {
                    await store.AppendEventAsync(runId, "retry_scheduled",
                        Serialize(new { runId, nodeId = node.Id, nextAttempt = attempt + 1, retryBackoffSeconds }),
                        cancellationToken);
                    if (retryBackoffSeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(retryBackoffSeconds), cancellationToken);
                    attempt++;
                    continue;
                }

                if (boundaryNode is not null)
                {
                    await store.AppendEventAsync(runId, "boundary_event_triggered",
                        Serialize(new { runId, boundaryNodeId = boundaryNode.Id, sourceNodeId = node.Id }),
                        cancellationToken);
                    await store.AppendEventAsync(runId, "node_completed",
                        Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName, reason = "retry_exhausted_boundary" }),
                        cancellationToken);
                    await store.UpdateStepStatusAsync(step.Id, FailedStatus, null, outcome.FailureReason, DateTime.UtcNow.ToString("o"), outcome.PolicyDecision, outcome.RuntimeSnapshot, cancellationToken);
                    return ServiceExecutionResult.Completed;
                }

                await store.AppendEventAsync(runId, "service_task_retry_exhausted",
                    Serialize(new { runId, nodeId = node.Id, stepId = step.Id, attempts = attempt }),
                    cancellationToken);
                await store.UpdateStepStatusAsync(step.Id, FailedStatus, null, outcome.FailureReason, DateTime.UtcNow.ToString("o"), outcome.PolicyDecision, outcome.RuntimeSnapshot, cancellationToken);
                return ServiceExecutionResult.Failed;
            }

            await store.AppendEventAsync(runId, "agent_output_recorded",
                Serialize(new { runId, nodeId = node.Id, stepId = step.Id, agent = metadata?.Agent, outputLength = outcome.Output?.Length ?? 0 }),
                cancellationToken);
            await store.AppendEventAsync(runId, "node_completed",
                Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName }),
                cancellationToken);
            await store.UpdateStepStatusAsync(step.Id, CompletedStatus, outcome.Output, null, DateTime.UtcNow.ToString("o"), outcome.PolicyDecision, outcome.RuntimeSnapshot, cancellationToken);

            // Persist the step's primary output to run context so later tasks can read it.
            if (!string.IsNullOrEmpty(outcome.Output))
            {
                await runContext.SetAsync(runId, $"output.{node.Id}", outcome.Output, RunContextKinds.Output, cancellationToken);
            }

            return ServiceExecutionResult.Completed;
        }
    }

    private async Task<WorkflowExecutionState> ScheduleTimerAndPauseAsync(
        string runId,
        BpmnNodeDefinition node,
        string? nextNodeId,
        CancellationToken cancellationToken)
    {
        var duration = ParseDuration(node.TimerDuration);
        var dueAt = DateTimeOffset.UtcNow.Add(duration);

        await _store.AppendEventAsync(runId, "timer_scheduled",
            Serialize(new { runId, nodeId = node.Id, dueAt = dueAt.ToString("o"), duration = node.TimerDuration ?? "PT0S" }),
            cancellationToken);

        await _store.UpdateRunStatusAsync(runId, WaitingTimerStatus, completedAt: null, cancellationToken);
        await SaveCheckpointAsync(runId, WaitingTimerStatus, nextNodeId, node.Id, completedAt: null, cancellationToken);

        return new WorkflowExecutionState(
            RunId: runId,
            Status: WaitingTimerStatus,
            NextNodeId: nextNodeId,
            WaitingOnNodeId: node.Id,
            CompletedAt: null,
            TimerDueAt: dueAt);
    }

    private async Task<WorkflowExecutionState> WaitForExternalEventAsync(
        string runId,
        BpmnNodeDefinition node,
        string? nextNodeId,
        CancellationToken cancellationToken)
    {
        var externalEvent = node.ExternalEventMetadata
            ?? throw new InvalidOperationException($"Node '{node.Id}' is missing external event metadata.");

        var correlationKey = await RenderCorrelationKeyAsync(runId, externalEvent.CorrelationKeyTemplate, cancellationToken);

        await _store.AppendEventAsync(runId, "external_event_waiting",
            Serialize(new
            {
                runId,
                nodeId = node.Id,
                messageName = externalEvent.MessageName,
                correlationKey
            }),
            cancellationToken);

        await _store.UpdateRunStatusAsync(runId, WaitingExternalStatus, completedAt: null, cancellationToken);
        await SaveCheckpointAsync(
            runId,
            WaitingExternalStatus,
            nextNodeId,
            node.Id,
            completedAt: null,
            cancellationToken,
            externalCorrelationKey: correlationKey,
            externalMessageName: externalEvent.MessageName);

        return new WorkflowExecutionState(
            RunId: runId,
            Status: WaitingExternalStatus,
            NextNodeId: nextNodeId,
            WaitingOnNodeId: node.Id,
            CompletedAt: null,
            WaitingExternalCorrelationKey: correlationKey,
            WaitingExternalMessageName: externalEvent.MessageName);
    }

    private Task ExecuteParallelTimerAsync(
        string runId,
        BpmnNodeDefinition node,
        CancellationToken cancellationToken,
        IWorkflowRuntimeStore? storeOverride = null)
    {
        var store = storeOverride ?? _store;
        return ExecuteParallelTimerCoreAsync(runId, node, store, cancellationToken);
    }

    private static async Task ExecuteParallelTimerCoreAsync(
        string runId,
        BpmnNodeDefinition node,
        IWorkflowRuntimeStore store,
        CancellationToken cancellationToken)
    {
        var duration = ParseDuration(node.TimerDuration);
        var dueAt = DateTimeOffset.UtcNow.Add(duration);

        await store.AppendEventAsync(runId, "timer_scheduled",
            Serialize(new { runId, nodeId = node.Id, dueAt = dueAt.ToString("o"), duration = node.TimerDuration ?? "PT0S" }),
            cancellationToken);

        if (duration > TimeSpan.Zero)
            await Task.Delay(duration, cancellationToken);

        await store.AppendEventAsync(runId, "timer_fired",
            Serialize(new { runId, nodeId = node.Id, firedAt = DateTime.UtcNow.ToString("o") }),
            cancellationToken);

        await store.AppendEventAsync(runId, "node_completed",
            Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName }),
            cancellationToken);
    }

    // ── Checkpoint helpers ────────────────────────────────────────────────────

    private async Task SaveCheckpointAsync(
        string runId,
        string status,
        string? nextNodeId,
        string? waitingOnNodeId,
        string? completedAt,
        CancellationToken cancellationToken,
        string? externalCorrelationKey = null,
        string? externalMessageName = null)
    {
        await _store.AppendEventAsync(runId, "checkpoint_saved",
            Serialize(new CheckpointPayload(status, nextNodeId, waitingOnNodeId, completedAt, externalCorrelationKey, externalMessageName)),
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

    /// <summary>
    /// Finds the artifact the nearest preceding service task produced, so a userTask
    /// approval gate can carry it forward for the approval card to render (#134).
    /// </summary>
    private async Task<string?> FindWaitingApprovalArtifactNameAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        string waitingOnNodeId,
        CancellationToken cancellationToken)
    {
        var precedingNode = definition.FindPrecedingServiceTaskNode(waitingOnNodeId);
        if (precedingNode is null)
        {
            return null;
        }

        var run = await _store.GetRunAsync(runId, cancellationToken);
        var precedingStep = run?.Steps.FirstOrDefault(s => s.RuntimeSnapshot?.NodeId == precedingNode.Id);
        return precedingStep?.RuntimeSnapshot?.Artifacts.FirstOrDefault()?.Name;
    }

    private async Task<string> RenderCorrelationKeyAsync(
        string runId,
        string template,
        CancellationToken cancellationToken)
    {
        var entries = await _runContext.GetAllAsync(runId, cancellationToken);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            variables[entry.Key] = entry.Value;
            variables[$"run_context.{entry.Key}"] = entry.Value;
        }

        return TemplateVariablePattern.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return variables.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    private async Task MergeExternalPayloadAsync(
        string runId,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken)
    {
        foreach (var pair in payload)
        {
            await _runContext.SetAsync(runId, $"event.{pair.Key}", pair.Value, RunContextKinds.External, cancellationToken);
        }
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static bool IsSupportedRuntimeNode(string elementName) =>
        elementName is "startEvent" or "serviceTask" or "userTask" or "receiveTask" or "endEvent" or
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

    private static TimeSpan ParseDuration(string? isoDuration)
    {
        if (string.IsNullOrWhiteSpace(isoDuration))
            return TimeSpan.Zero;

        try
        {
            return System.Xml.XmlConvert.ToTimeSpan(isoDuration);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);

    private sealed record CheckpointPayload(
        string Status,
        string? NextNodeId,
        string? WaitingOnNodeId,
        string? CompletedAt,
        string? ExternalCorrelationKey,
        string? ExternalMessageName);

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

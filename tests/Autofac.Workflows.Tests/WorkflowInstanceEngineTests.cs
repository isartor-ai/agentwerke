using Autofac.Domain.Persistence;
using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;

namespace Autofac.Workflows.Tests;

public sealed class WorkflowInstanceEngineTests
{
    [Fact]
    public async Task StartAsync_ForReferenceWorkflow_ProgressesDeterministicallyToUserTaskCheckpoint()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor());

        var state = await engine.StartAsync(
            workflowDefinitionId: Guid.NewGuid().ToString(),
            definition: CreateReferenceDefinition(),
            initiator: "system",
            cancellationToken: CancellationToken.None);

        Assert.Equal("waiting_user", state.Status);
        Assert.Equal("HumanApproval", state.WaitingOnNodeId);
        Assert.Equal(3, state.NextNodeIndex);

        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.Type == "checkpoint_saved" && e.Message.Contains("\"status\":\"running\"", StringComparison.Ordinal));
        Assert.Contains(events, static e => e.Type == "checkpoint_saved" && e.Message.Contains("\"status\":\"waiting_user\"", StringComparison.Ordinal));

        var eventTypes = events.Select(static e => e.Type).ToList();
        Assert.Contains("run_started", eventTypes);
        Assert.Contains("user_task_waiting", eventTypes);
        Assert.Contains(events, static e =>
            e.Type == "user_task_waiting" &&
            e.Message.Contains("\"purposeType\":\"manual_review\"", StringComparison.Ordinal) &&
            e.Message.Contains("\"policyTag\":\"human_approval_required\"", StringComparison.Ordinal));

        var firstRunStarted = eventTypes.FindIndex(static t => string.Equals(t, "run_started", StringComparison.Ordinal));
        var firstUserTaskWaiting = eventTypes.FindIndex(static t => string.Equals(t, "user_task_waiting", StringComparison.Ordinal));

        Assert.True(firstRunStarted >= 0);
        Assert.True(firstUserTaskWaiting > firstRunStarted);
    }

    [Fact]
    public async Task ResumeAsync_AfterUserTaskApproval_CompletesRun()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var definition = CreateReferenceDefinition();
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor());

        var started = await engine.StartAsync(Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);
        var resumed = await engine.ResumeAsync(started.RunId, definition, "reviewer", CancellationToken.None);

        Assert.Equal("completed", resumed.Status);
        Assert.Null(resumed.WaitingOnNodeId);
        Assert.NotNull(resumed.CompletedAt);

        var persistedRun = await store.GetRunAsync(started.RunId, CancellationToken.None);
        Assert.NotNull(persistedRun);
        Assert.Equal("completed", persistedRun!.Status);
        Assert.NotNull(persistedRun.CompletedAt);

        var events = await store.ListRunEventsAsync(started.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.Type == "user_task_completed");
        Assert.Contains(events, static e => e.Type == "run_completed");
    }

    [Fact]
    public async Task RecoverAsync_WhenRestartOccursAtUserTask_RestoresCheckpointState()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var definition = CreateReferenceDefinition();

        var engine1 = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor());
        var started = await engine1.StartAsync(Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        var engine2 = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor());
        var recovered = await engine2.RecoverAsync(started.RunId, definition, CancellationToken.None);

        Assert.Equal(started.RunId, recovered.RunId);
        Assert.Equal("waiting_user", recovered.Status);
        Assert.Equal("HumanApproval", recovered.WaitingOnNodeId);
        Assert.Equal(3, recovered.NextNodeIndex);
    }

    [Fact]
    public async Task StartAsync_WhenServiceTaskHasRetries_CompletesAfterRetrySequence()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor());

        var definition = new BpmnWorkflowDefinition(
            ProcessId: "RetryFlow",
            ProcessName: "Retry Flow",
            Nodes:
            [
                new BpmnNodeDefinition("Start", "Start", "startEvent", null),
                new BpmnNodeDefinition(
                    "Deploy",
                    "Deploy",
                    "serviceTask",
                    new AutofacTaskMetadata("agent", "action", null, "purpose", "policy", [], MaxRetries: 2, RetryBackoffSeconds: 1, FailUntilAttempt: 2)),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ]);

        var state = await engine.StartAsync(Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("completed", state.Status);
        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Equal(3, events.Count(static e => e.Type == "service_task_attempted"));
        Assert.Equal(2, events.Count(static e => e.Type == "retry_scheduled"));
        Assert.Contains(events, static e =>
            e.Type == "service_task_attempted" &&
            e.Message.Contains("\"agent\":\"agent\"", StringComparison.Ordinal) &&
            e.Message.Contains("\"action\":\"action\"", StringComparison.Ordinal) &&
            e.Message.Contains("\"purposeType\":\"purpose\"", StringComparison.Ordinal) &&
            e.Message.Contains("\"policyTag\":\"policy\"", StringComparison.Ordinal));
        Assert.Contains(events, static e => e.Type == "run_completed");
    }

    [Fact]
    public async Task StartAsync_WhenServiceTaskTimeoutTriggersBoundary_ContinuesToCompletion()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor());

        var definition = new BpmnWorkflowDefinition(
            ProcessId: "TimeoutFlow",
            ProcessName: "Timeout Flow",
            Nodes:
            [
                new BpmnNodeDefinition("Start", "Start", "startEvent", null),
                new BpmnNodeDefinition(
                    "LongTask",
                    "Long Task",
                    "serviceTask",
                    new AutofacTaskMetadata("agent", "action", null, "purpose", "policy", [], SimulateTimeout: true, TimeoutSeconds: 5)),
                new BpmnNodeDefinition("TaskTimeoutBoundary", "Task Timeout", "boundaryEvent", null),
                new BpmnNodeDefinition("RecoveryTask", "Recover", "serviceTask", new AutofacTaskMetadata("agent", "action", null, "purpose", "policy", [])),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ]);

        var state = await engine.StartAsync(Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("completed", state.Status);
        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.Type == "timeout_triggered");
        Assert.Contains(events, static e => e.Type == "boundary_event_triggered");
        Assert.Contains(events, static e => e.Type == "run_completed");
    }

    [Fact]
    public async Task StartAsync_WhenParallelGatewayExists_ExecutesParallelSectionAndJoins()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor());

        var definition = new BpmnWorkflowDefinition(
            ProcessId: "ParallelFlow",
            ProcessName: "Parallel Flow",
            Nodes:
            [
                new BpmnNodeDefinition("Start", "Start", "startEvent", null),
                new BpmnNodeDefinition("Fork", "Fork", "parallelGateway", null),
                new BpmnNodeDefinition("BranchA", "Branch A", "serviceTask", new AutofacTaskMetadata("agent", "action", null, "purpose", "policy", [])),
                new BpmnNodeDefinition("BranchB", "Branch B", "serviceTask", new AutofacTaskMetadata("agent", "action", null, "purpose", "policy", [])),
                new BpmnNodeDefinition("Join", "Join", "parallelGateway", null),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ]);

        var state = await engine.StartAsync(Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("completed", state.Status);
        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.Type == "parallel_forked");
        Assert.Equal(2, events.Count(static e => e.Type == "parallel_branch_entered"));
        Assert.Contains(events, static e => e.Type == "parallel_joined");
        Assert.Contains(events, static e => e.Type == "run_completed");
    }

    private static BpmnWorkflowDefinition CreateReferenceDefinition()
    {
        return new BpmnWorkflowDefinition(
            ProcessId: "Reference",
            ProcessName: "Reference Workflow",
            Nodes:
            [
                new BpmnNodeDefinition("Start", "Start", "startEvent", null),
                new BpmnNodeDefinition("Deploy", "Deploy", "serviceTask", new AutofacTaskMetadata("agent", "action", null, "purpose", "policy", [])),
                new BpmnNodeDefinition(
                    "HumanApproval",
                    "Approval",
                    "userTask",
                    null,
                    new AutofacApprovalMetadata("manual_review", "human_approval_required")),
                new BpmnNodeDefinition("Finalize", "Finalize", "serviceTask", new AutofacTaskMetadata("agent", "action", null, "purpose", "policy", [])),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ]);
    }

    private sealed class InMemoryWorkflowRuntimeStore : IWorkflowRuntimeStore
    {
        private readonly Dictionary<string, WorkflowRun> _runs = [];
        private readonly object _sync = new();

        public Task<WorkflowRun> CreateRunAsync(string workflowDefinitionId, string? initiator, CancellationToken cancellationToken)
        {
            var run = new WorkflowRun
            {
                Id = Guid.NewGuid().ToString(),
                WorkflowId = workflowDefinitionId,
                Status = "created",
                RequestedBy = initiator ?? "unknown",
                StartedAt = DateTime.UtcNow.ToString("o")
            };

            lock (_sync)
            {
                _runs[run.Id] = run;
            }

            return Task.FromResult(run);
        }

        public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _runs.TryGetValue(runId, out var run);
                return Task.FromResult(run);
            }
        }

        public Task<IReadOnlyList<WorkflowEvent>> ListRunEventsAsync(string runId, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_runs.TryGetValue(runId, out var run))
                {
                    return Task.FromResult<IReadOnlyList<WorkflowEvent>>(run.Events.AsReadOnly());
                }
                return Task.FromResult<IReadOnlyList<WorkflowEvent>>(new List<WorkflowEvent>().AsReadOnly());
            }
        }

        public Task AppendEventAsync(string runId, string type, string message, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_runs.TryGetValue(runId, out var run))
                {
                    run.Events.Add(new WorkflowEvent
                    {
                        Id = Guid.NewGuid().ToString(),
                        Type = type,
                        Message = message,
                        CreatedAt = DateTime.UtcNow.ToString("o")
                    });
                }
            }

            return Task.CompletedTask;
        }

        public Task UpdateRunStatusAsync(string runId, string status, string? completedAt, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_runs.TryGetValue(runId, out var run))
                {
                    throw new InvalidOperationException($"Run {runId} does not exist.");
                }

                run.Status = status;
                run.CompletedAt = completedAt;
            }

            return Task.CompletedTask;
        }

        public Task<WorkflowRunStep> CreateStepAsync(
            string runId, string nodeId, string? nodeName, string nodeType,
            string? agentName, CancellationToken cancellationToken)
        {
            var step = new WorkflowRunStep
            {
                Id = $"step_{Guid.NewGuid():N}",
                Name = nodeName ?? nodeId,
                Type = nodeType,
                Status = "running",
                StartedAt = DateTime.UtcNow.ToString("o"),
                AgentName = agentName
            };

            lock (_sync)
            {
                if (_runs.TryGetValue(runId, out var run))
                {
                    run.Steps.Add(step);
                }
            }

            return Task.FromResult(step);
        }

        public Task UpdateStepStatusAsync(
            string stepId, string status, string? output, string? completedAt,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpServiceTaskExecutor : IServiceTaskExecutor
    {
        public Task<AgentTaskOutcome> ExecuteAsync(
            string runId, string stepId, BpmnNodeDefinition node,
            int attempt, CancellationToken cancellationToken)
        {
            var metadata = node.Metadata;

            if (metadata is not null && attempt <= metadata.FailUntilAttempt)
            {
                return Task.FromResult(new AgentTaskOutcome(
                    Succeeded: false,
                    Output: null,
                    FailureReason: $"Simulated failure on attempt {attempt}"));
            }

            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: true,
                Output: "no-op executor",
                FailureReason: null));
        }
    }
}
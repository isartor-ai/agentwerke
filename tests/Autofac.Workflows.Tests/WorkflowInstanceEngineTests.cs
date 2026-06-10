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
        var engine = new WorkflowInstanceEngine(store);

        var state = await engine.StartAsync(
            workflowDefinitionId: Guid.NewGuid(),
            definition: CreateReferenceDefinition(),
            initiator: "system",
            cancellationToken: CancellationToken.None);

        Assert.Equal("waiting_user", state.Status);
        Assert.Equal("HumanApproval", state.WaitingOnNodeId);
        Assert.Equal(3, state.NextNodeIndex);

        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.EventType == "checkpoint_saved" && e.PayloadJson.Contains("\"status\":\"running\"", StringComparison.Ordinal));
        Assert.Contains(events, static e => e.EventType == "checkpoint_saved" && e.PayloadJson.Contains("\"status\":\"waiting_user\"", StringComparison.Ordinal));

        var eventTypes = events.Select(static e => e.EventType).ToList();
        Assert.Contains("run_started", eventTypes);
        Assert.Contains("user_task_waiting", eventTypes);

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
        var engine = new WorkflowInstanceEngine(store);

        var started = await engine.StartAsync(Guid.NewGuid(), definition, "system", CancellationToken.None);
        var resumed = await engine.ResumeAsync(started.RunId, definition, "reviewer", CancellationToken.None);

        Assert.Equal("completed", resumed.Status);
        Assert.Null(resumed.WaitingOnNodeId);
        Assert.NotNull(resumed.CompletedAtUtc);

        var persistedRun = await store.GetRunAsync(started.RunId, CancellationToken.None);
        Assert.NotNull(persistedRun);
        Assert.Equal("completed", persistedRun!.Status);
        Assert.NotNull(persistedRun.CompletedAtUtc);

        var events = await store.ListRunEventsAsync(started.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.EventType == "user_task_completed");
        Assert.Contains(events, static e => e.EventType == "run_completed");
    }

    [Fact]
    public async Task RecoverAsync_WhenRestartOccursAtUserTask_RestoresCheckpointState()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var definition = CreateReferenceDefinition();

        var engine1 = new WorkflowInstanceEngine(store);
        var started = await engine1.StartAsync(Guid.NewGuid(), definition, "system", CancellationToken.None);

        var engine2 = new WorkflowInstanceEngine(store);
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
        var engine = new WorkflowInstanceEngine(store);

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

        var state = await engine.StartAsync(Guid.NewGuid(), definition, "system", CancellationToken.None);

        Assert.Equal("completed", state.Status);
        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Equal(3, events.Count(static e => e.EventType == "service_task_attempted"));
        Assert.Equal(2, events.Count(static e => e.EventType == "retry_scheduled"));
        Assert.Contains(events, static e => e.EventType == "run_completed");
    }

    [Fact]
    public async Task StartAsync_WhenServiceTaskTimeoutTriggersBoundary_ContinuesToCompletion()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store);

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

        var state = await engine.StartAsync(Guid.NewGuid(), definition, "system", CancellationToken.None);

        Assert.Equal("completed", state.Status);
        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.EventType == "timeout_triggered");
        Assert.Contains(events, static e => e.EventType == "boundary_event_triggered");
        Assert.Contains(events, static e => e.EventType == "run_completed");
    }

    [Fact]
    public async Task StartAsync_WhenParallelGatewayExists_ExecutesParallelSectionAndJoins()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store);

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

        var state = await engine.StartAsync(Guid.NewGuid(), definition, "system", CancellationToken.None);

        Assert.Equal("completed", state.Status);
        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.EventType == "parallel_forked");
        Assert.Equal(2, events.Count(static e => e.EventType == "parallel_branch_entered"));
        Assert.Contains(events, static e => e.EventType == "parallel_joined");
        Assert.Contains(events, static e => e.EventType == "run_completed");
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
                new BpmnNodeDefinition("HumanApproval", "Approval", "userTask", null),
                new BpmnNodeDefinition("Finalize", "Finalize", "serviceTask", new AutofacTaskMetadata("agent", "action", null, "purpose", "policy", [])),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ]);
    }

    private sealed class InMemoryWorkflowRuntimeStore : IWorkflowRuntimeStore
    {
        private readonly Dictionary<Guid, WorkflowRun> _runs = [];
        private readonly List<WorkflowEvent> _events = [];
        private readonly object _sync = new();

        public Task<WorkflowRun> CreateRunAsync(Guid workflowDefinitionId, string? initiator, CancellationToken cancellationToken)
        {
            var run = new WorkflowRun
            {
                Id = Guid.NewGuid(),
                WorkflowDefinitionId = workflowDefinitionId,
                Status = "created",
                Initiator = initiator,
                StartedAtUtc = DateTimeOffset.UtcNow
            };

            lock (_sync)
            {
                _runs[run.Id] = run;
            }

            return Task.FromResult(run);
        }

        public Task<WorkflowRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _runs.TryGetValue(runId, out var run);
                return Task.FromResult(run);
            }
        }

        public Task<IReadOnlyList<WorkflowEvent>> ListRunEventsAsync(Guid runId, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var events = _events
                    .Where(runEvent => runEvent.WorkflowRunId == runId)
                    .OrderBy(runEvent => runEvent.CreatedAtUtc)
                    .ThenBy(runEvent => runEvent.Id)
                    .ToList();

                return Task.FromResult<IReadOnlyList<WorkflowEvent>>(events);
            }
        }

        public Task AppendEventAsync(Guid runId, string eventType, string payloadJson, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _events.Add(new WorkflowEvent
                {
                    Id = Guid.NewGuid(),
                    WorkflowRunId = runId,
                    EventType = eventType,
                    PayloadJson = payloadJson,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }

            return Task.CompletedTask;
        }

        public Task UpdateRunStatusAsync(Guid runId, string status, DateTimeOffset? completedAtUtc, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_runs.TryGetValue(runId, out var run))
                {
                    throw new InvalidOperationException($"Run {runId} does not exist.");
                }

                run.Status = status;
                run.CompletedAtUtc = completedAtUtc;
            }

            return Task.CompletedTask;
        }
    }
}
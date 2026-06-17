using Autofac.Domain.AgentRuntime;
using Autofac.Domain.Persistence;
using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autofac.Workflows.Tests;

public sealed class WorkflowInstanceEngineTests
{
    [Fact]
    public async Task StartAsync_ForReferenceWorkflow_ProgressesDeterministicallyToUserTaskCheckpoint()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

        var state = await engine.StartAsync(
            workflowDefinitionId: Guid.NewGuid().ToString(),
            definition: CreateReferenceDefinition(),
            initiator: "system",
            cancellationToken: CancellationToken.None);

        Assert.Equal("waiting_user", state.Status);
        Assert.Equal("HumanApproval", state.WaitingOnNodeId);
        Assert.Equal("Finalize", state.NextNodeId);

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
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

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

        var engine1 = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);
        var started = await engine1.StartAsync(Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        var engine2 = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);
        var recovered = await engine2.RecoverAsync(started.RunId, definition, CancellationToken.None);

        Assert.Equal(started.RunId, recovered.RunId);
        Assert.Equal("waiting_user", recovered.Status);
        Assert.Equal("HumanApproval", recovered.WaitingOnNodeId);
        Assert.Equal("Finalize", recovered.NextNodeId);
    }

    [Fact]
    public async Task StartAsync_WhenTimerCatchEventIsReached_ReturnsWaitingTimerWithDueAt()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

        var definition = new BpmnWorkflowDefinition(
            ProcessId: "TimerFlow",
            ProcessName: "Timer Flow",
            Nodes:
            [
                new BpmnNodeDefinition("Start", "Start", "startEvent", null),
                new BpmnNodeDefinition("Wait", "Wait", "intermediateCatchEvent", null, TimerDuration: "PT5S"),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ]);

        var before = DateTimeOffset.UtcNow;
        var state = await engine.StartAsync(Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("waiting_timer", state.Status);
        Assert.Equal("Wait", state.WaitingOnNodeId);
        Assert.Equal("End", state.NextNodeId);
        Assert.NotNull(state.TimerDueAt);
        Assert.True(state.TimerDueAt >= before.AddSeconds(4));
    }

    [Fact]
    public async Task StartAsync_WhenExistingRunIdIsProvided_ReusesPreCreatedRun()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);
        var definition = CreateReferenceDefinition();
        var preCreatedRun = await store.CreateRunAsync("wf-existing", "system", CancellationToken.None);

        var state = await engine.StartAsync(
            new WorkflowEngineStartRequest("wf-existing", definition, "system", ExistingRunId: preCreatedRun.Id),
            CancellationToken.None);

        Assert.Equal(preCreatedRun.Id, state.RunId);
        var persistedRun = await store.GetRunAsync(preCreatedRun.Id, CancellationToken.None);
        Assert.NotNull(persistedRun);
        Assert.Equal(preCreatedRun.Id, persistedRun!.Id);
    }

    [Fact]
    public async Task StartAsync_WhenServiceTaskHasRetries_CompletesAfterRetrySequence()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

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
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

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
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

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

    [Fact]
    public async Task StartAsync_WhenServiceTaskReturnsRuntimeSnapshot_PersistsSnapshotOnStep()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new RuntimeSnapshotServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

        var definition = new BpmnWorkflowDefinition(
            ProcessId: "RuntimeSnapshotFlow",
            ProcessName: "Runtime Snapshot Flow",
            Nodes:
            [
                new BpmnNodeDefinition("Start", "Start", "startEvent", null),
                new BpmnNodeDefinition("Deploy", "Deploy", "serviceTask", new AutofacTaskMetadata("deploy-agent", "deploy", null, "purpose", "policy", [])),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ]);

        var state = await engine.StartAsync(Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        var persistedRun = await store.GetRunAsync(state.RunId, CancellationToken.None);
        var serviceStep = Assert.Single(persistedRun!.Steps);
        Assert.Equal("completed", serviceStep.Status);
        Assert.NotNull(serviceStep.RuntimeSnapshot);
        Assert.Equal("deploy-agent", serviceStep.RuntimeSnapshot!.AgentName);
        Assert.Equal(AgentPermissionLevels.ReadWrite, serviceStep.RuntimeSnapshot.Contract.Permissions.Level);
    }

    [Fact]
    public async Task StartAsync_WhenServiceTaskFails_SetsStepErrorNotOutput()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new AlwaysFailingServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

        var definition = new BpmnWorkflowDefinition(
            ProcessId: "FailFlow",
            ProcessName: "Fail Flow",
            Nodes:
            [
                new BpmnNodeDefinition("Start", "Start", "startEvent", null),
                new BpmnNodeDefinition("FailTask", "Fail Task", "serviceTask", new AutofacTaskMetadata("agent", "action", null, "purpose", "policy", [])),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ]);

        var state = await engine.StartAsync(Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("failed", state.Status);
        var persistedRun = await store.GetRunAsync(state.RunId, CancellationToken.None);
        var failedStep = Assert.Single(persistedRun!.Steps);
        Assert.Equal("failed", failedStep.Status);
        Assert.Null(failedStep.Output);
        Assert.NotNull(failedStep.Error);
        Assert.Contains("always fails", failedStep.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAsync_WhenServiceTaskSucceeds_SetsStepOutputNotError()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new NoOpServiceTaskExecutor(), new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

        var definition = new BpmnWorkflowDefinition(
            ProcessId: "SucceedFlow",
            ProcessName: "Succeed Flow",
            Nodes:
            [
                new BpmnNodeDefinition("Start", "Start", "startEvent", null),
                new BpmnNodeDefinition("Task", "Task", "serviceTask", new AutofacTaskMetadata("agent", "action", null, "purpose", "policy", [])),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ]);

        var state = await engine.StartAsync(Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("completed", state.Status);
        var persistedRun = await store.GetRunAsync(state.RunId, CancellationToken.None);
        var step = Assert.Single(persistedRun!.Steps);
        Assert.Equal("completed", step.Status);
        Assert.NotNull(step.Output);
        Assert.Null(step.Error);
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

    private sealed class AlwaysFailingServiceTaskExecutor : IServiceTaskExecutor
    {
        public Task<AgentTaskOutcome> ExecuteAsync(
            string runId, string stepId, BpmnNodeDefinition node,
            int attempt, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: "This executor always fails."));
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

    private sealed class RuntimeSnapshotServiceTaskExecutor : IServiceTaskExecutor
    {
        public Task<AgentTaskOutcome> ExecuteAsync(
            string runId, string stepId, BpmnNodeDefinition node,
            int attempt, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: true,
                Output: "runtime snapshot executor",
                FailureReason: null,
                RuntimeSnapshot: new AgentRuntimeSnapshot
                {
                    RunId = runId,
                    StepId = stepId,
                    NodeId = node.Id,
                    AgentName = node.Metadata?.Agent,
                    Action = node.Metadata?.Action,
                    Contract = new AgentRuntimeContract
                    {
                        Permissions = new AgentPermissionContract
                        {
                            Level = AgentPermissionLevels.ReadWrite
                        }
                    }
                }));
        }
    }
}

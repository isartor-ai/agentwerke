using Autofac.Domain.Persistence;
using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autofac.Workflows.Tests;

public sealed class WorkflowExternalActionTests
{
    [Fact]
    public async Task StartAsync_WhenServiceTaskReturnsExternalActions_RecordsConnectorEvents()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var engine = new WorkflowInstanceEngine(store, new ExternalActionServiceTaskExecutor(), NullLogger<WorkflowInstanceEngine>.Instance);

        var state = await engine.StartAsync(
            Guid.NewGuid().ToString(),
            CreateDefinition(),
            "system",
            CancellationToken.None);

        Assert.Equal("completed", state.Status);

        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e =>
            e.Type == "external_action_recorded" &&
            e.Message.Contains("\"provider\":\"github\"", StringComparison.Ordinal) &&
            e.Message.Contains("\"action\":\"create_pull_request\"", StringComparison.Ordinal) &&
            e.Message.Contains("\"resourceUrl\":\"https://github.com/octo/autofac/pull/42\"", StringComparison.Ordinal));
    }

    private static BpmnWorkflowDefinition CreateDefinition()
    {
        return new BpmnWorkflowDefinition(
            ProcessId: "ExternalActionFlow",
            ProcessName: "External Action Flow",
            Nodes:
            [
                new BpmnNodeDefinition("Start", "Start", "startEvent", null),
                new BpmnNodeDefinition(
                    "OpenPr",
                    "Open Pull Request",
                    "serviceTask",
                    new AutofacTaskMetadata("github-agent", "github.create_pull_request", null, "implementation", "repo-change", [])),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ]);
    }

    private sealed class ExternalActionServiceTaskExecutor : IServiceTaskExecutor
    {
        public Task<AgentTaskOutcome> ExecuteAsync(
            string runId,
            string stepId,
            BpmnNodeDefinition node,
            int attempt,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: true,
                Output: "created branch and PR",
                FailureReason: null,
                Artifacts: null,
                ExternalActions:
                [
                    new ExternalActionRecord(
                        Provider: "github",
                        Action: "create_pull_request",
                        Status: "completed",
                        ResourceId: "42",
                        ResourceUrl: "https://github.com/octo/autofac/pull/42",
                        Summary: "Opened pull request #42")
                ]));
        }
    }

    private sealed class InMemoryWorkflowRuntimeStore : IWorkflowRuntimeStore
    {
        private readonly Dictionary<string, WorkflowRun> _runs = [];
        private readonly object _sync = new();

        public Task<WorkflowRun> CreateRunAsync(string workflowDefinitionId, string? initiator, CancellationToken cancellationToken, string? correlationId = null)
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

                return Task.FromResult<IReadOnlyList<WorkflowEvent>>(Array.Empty<WorkflowEvent>());
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
                if (_runs.TryGetValue(runId, out var run))
                {
                    run.Status = status;
                    run.CompletedAt = completedAt;
                }
            }

            return Task.CompletedTask;
        }

        public Task<WorkflowRunStep> CreateStepAsync(
            string runId,
            string nodeId,
            string? nodeName,
            string nodeType,
            string? agentName,
            CancellationToken cancellationToken)
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
            string stepId,
            string status,
            string? output,
            string? completedAt,
            PolicyDecision? policyDecision,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

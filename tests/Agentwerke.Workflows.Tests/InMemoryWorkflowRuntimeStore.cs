using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;
using Agentwerke.Workflows.Runtime;

namespace Agentwerke.Workflows.Tests;

/// <summary>
/// In-memory implementation of <see cref="IWorkflowRuntimeStore"/> shared by engine and
/// conformance tests. All state lives in process with no external dependencies.
/// </summary>
internal sealed class InMemoryWorkflowRuntimeStore : IWorkflowRuntimeStore
{
    private readonly Dictionary<string, WorkflowRun> _runs = [];
    private readonly object _sync = new();

    public Task<WorkflowRun> CreateRunAsync(
        string workflowDefinitionId, string? initiator, CancellationToken cancellationToken,
        string? correlationId = null)
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

    public Task<IReadOnlyList<WorkflowEvent>> ListRunEventsAsync(
        string runId, CancellationToken cancellationToken)
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

    public Task AppendEventAsync(
        string runId, string type, string message, CancellationToken cancellationToken)
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

    public Task UpdateRunStatusAsync(
        string runId, string status, string? completedAt, CancellationToken cancellationToken)
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
        string stepId, string status, string? output, string? error, string? completedAt,
        PolicyDecision? policyDecision,
        AgentRuntimeSnapshot? runtimeSnapshot,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            var step = _runs.Values
                .SelectMany(static run => run.Steps)
                .FirstOrDefault(s => string.Equals(s.Id, stepId, StringComparison.Ordinal));

            if (step is not null)
            {
                step.Status = status;
                step.Output = output;
                step.Error = error;
                step.CompletedAt = completedAt;
                step.PolicyDecision = policyDecision;
                step.RuntimeSnapshot = runtimeSnapshot;
            }
        }

        return Task.CompletedTask;
    }
}

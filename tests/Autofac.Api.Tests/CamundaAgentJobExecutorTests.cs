using System.Text.Json;
using Autofac.Application.Workflows;
using Autofac.Domain.AgentRuntime;
using Autofac.Domain.Persistence;
using Autofac.Infrastructure;
using Autofac.Infrastructure.Workers;
using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.Logging;

namespace Autofac.Api.Tests;

public sealed class CamundaAgentJobExecutorTests
{
    [Fact]
    public async Task Dispatcher_ProcessNextBatchAsync_ActivatesAutofacAgentJobsAndHandsThemToExecutor()
    {
        var camundaClient = new RecordingCamundaClient
        {
            ActivatedJobs = [CreateJob()]
        };
        var executor = new RecordingCamundaAgentJobExecutor();
        var logger = new ListLogger<CamundaAgentJobDispatcher>();
        var sut = new CamundaAgentJobDispatcher(
            camundaClient,
            executor,
            Microsoft.Extensions.Options.Options.Create(new CamundaOptions
            {
                Enabled = true,
                BaseUrl = "https://camunda.example.test/",
                AuthMode = CamundaAuthMode.None
            }),
            logger);

        var count = await sut.ProcessNextBatchAsync(CancellationToken.None);

        Assert.Equal(1, count);
        Assert.NotNull(camundaClient.LastActivationRequest);
        Assert.Equal("autofac.agent", camundaClient.LastActivationRequest!.Type);
        Assert.Equal(1, camundaClient.LastActivationRequest.MaxJobsToActivate);
        Assert.Equal(["autofac", "input", "output"], camundaClient.LastActivationRequest.FetchVariables);
        Assert.Single(executor.Jobs);
        Assert.Equal("2251799813685402", executor.Jobs[0].JobKey);
    }

    [Fact]
    public async Task ExecuteAsync_CompletesJobAndRecordsWorkerLifecycle()
    {
        var runs = new InMemoryWorkflowRunState();
        var runRepository = new InMemoryWorkflowRunRepository(runs);
        var runtimeStore = new InMemoryWorkflowRuntimeStore(runs);
        var runContext = new InMemoryRunContextRepository();
        var camundaClient = new RecordingCamundaClient();
        var logger = new ListLogger<CamundaAgentJobExecutor>();

        await runRepository.CreatePendingRunAsync(
            runId: "run_123",
            workflowId: "wf_123",
            workflowName: "Workflow",
            workflowVersion: "v1",
            initiator: "tester",
            tags: [],
            correlationId: "corr_123",
            camunda: new WorkflowRunCamundaLink(
                ProcessInstanceKey: "2251799813685401",
                ProcessDefinitionKey: "2251799813685311",
                ProcessDefinitionId: "process-invoice",
                ProcessDefinitionVersion: 2),
            cancellationToken: CancellationToken.None);

        var sut = new CamundaAgentJobExecutor(
            runtimeStore,
            runRepository,
            runContext,
            new SuccessfulServiceTaskExecutor("generated diff"),
            camundaClient,
            logger);

        await sut.ExecuteAsync(CreateJob(), CancellationToken.None);

        var run = await runRepository.GetRunAsync("run_123", CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("running", run!.Status);
        Assert.True(string.IsNullOrEmpty(run.CurrentStep));

        var step = Assert.Single(run.Steps);
        Assert.Equal("completed", step.Status);
        Assert.Equal("generated diff", step.Output);
        Assert.Equal("implementation-agent", step.AgentName);

        Assert.Single(camundaClient.Completions);
        var completion = camundaClient.Completions[0];
        Assert.Equal("2251799813685402", completion.JobKey);
        Assert.Equal("run_123", completion.Request.Variables.GetProperty("autofac").GetProperty("runId").GetString());
        Assert.Equal("2251799813685402", completion.Request.Variables.GetProperty("autofac").GetProperty("lastJobKey").GetString());
        Assert.Equal(1, completion.Request.Variables.GetProperty("autofac").GetProperty("lastAttempt").GetInt32());
        Assert.Equal("generated diff", completion.Request.Variables.GetProperty("output").GetProperty("ImplementTask").GetString());

        var events = run.Events.Select(static e => e.Type).ToList();
        Assert.Contains("camunda_job_activated", events);
        Assert.Contains("service_task_attempted", events);
        Assert.Contains("agent_output_recorded", events);
        Assert.Contains("node_completed", events);
        Assert.Contains("camunda_job_completed", events);

        var contexts = await runContext.GetAllAsync("run_123", CancellationToken.None);
        Assert.Contains(contexts, entry => entry.Key == "output.ImplementTask" && entry.Value == "generated diff");
        Assert.Contains(contexts, entry => entry.Key == "camunda.attempt.ImplementTask" && entry.Value == "1");

        Assert.Contains(logger.Entries, entry =>
            entry.LogLevel == LogLevel.Information &&
            entry.State.TryGetValue("JobKey", out var jobKey) &&
            string.Equals(jobKey?.ToString(), "2251799813685402", StringComparison.Ordinal) &&
            entry.State.TryGetValue("RunId", out var runId) &&
            string.Equals(runId?.ToString(), "run_123", StringComparison.Ordinal) &&
            entry.State.TryGetValue("ElementId", out var elementId) &&
            string.Equals(elementId?.ToString(), "ImplementTask", StringComparison.Ordinal) &&
            entry.State.TryGetValue("AgentId", out var agentId) &&
            string.Equals(agentId?.ToString(), "implementation-agent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WhenAgentExecutionFails_RecordsFailureWithoutCompletingCamundaJob()
    {
        var runs = new InMemoryWorkflowRunState();
        var runRepository = new InMemoryWorkflowRunRepository(runs);
        var runtimeStore = new InMemoryWorkflowRuntimeStore(runs);
        var runContext = new InMemoryRunContextRepository();
        var camundaClient = new RecordingCamundaClient();
        var logger = new ListLogger<CamundaAgentJobExecutor>();

        await runRepository.CreatePendingRunAsync(
            runId: "run_123",
            workflowId: "wf_123",
            workflowName: "Workflow",
            workflowVersion: "v1",
            initiator: "tester",
            tags: [],
            correlationId: "corr_123",
            camunda: null,
            cancellationToken: CancellationToken.None);

        var sut = new CamundaAgentJobExecutor(
            runtimeStore,
            runRepository,
            runContext,
            new FailingServiceTaskExecutor("policy denied"),
            camundaClient,
            logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteAsync(CreateJob(), CancellationToken.None));

        Assert.Equal("policy denied", exception.Message);
        Assert.Empty(camundaClient.Completions);

        var run = await runRepository.GetRunAsync("run_123", CancellationToken.None);
        Assert.NotNull(run);
        var step = Assert.Single(run!.Steps);
        Assert.Equal("failed", step.Status);
        Assert.Equal("policy denied", step.Error);

        Assert.Contains(run.Events, eventRecord =>
            eventRecord.Type == "service_task_failed" &&
            eventRecord.Message.Contains("\"reason\":\"policy denied\"", StringComparison.Ordinal));
    }

    private static CamundaActivatedJob CreateJob()
    {
        return new CamundaActivatedJob
        {
            JobKey = "2251799813685402",
            Type = "autofac.agent",
            ProcessInstanceKey = "2251799813685401",
            ProcessDefinitionId = "process-invoice",
            ProcessDefinitionVersion = 2,
            ProcessDefinitionKey = "2251799813685311",
            ElementId = "ImplementTask",
            ElementInstanceKey = "2251799813685403",
            CustomHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["autofac.elementId"] = "ImplementTask",
                ["autofac.agent"] = "implementation-agent",
                ["autofac.action"] = "code.generate",
                ["autofac.environment"] = "repo",
                ["autofac.purposeType"] = "implementation",
                ["autofac.policyTag"] = "repo-change",
                ["autofac.requiresEvidence"] = "diff,test-results"
            },
            Worker = "autofac-worker",
            Retries = 3,
            Deadline = "1737067200000",
            Variables = JsonSerializer.SerializeToElement(new
            {
                autofac = new
                {
                    runId = "run_123",
                    workflowId = "wf_123"
                }
            }),
            TenantId = "<default>"
        };
    }

    private sealed class InMemoryWorkflowRunState
    {
        private readonly Dictionary<string, WorkflowRun> _runs = new(StringComparer.Ordinal);
        private readonly object _sync = new();

        public WorkflowRun AddRun(WorkflowRun run)
        {
            lock (_sync)
            {
                _runs[run.Id] = run;
                return run;
            }
        }

        public WorkflowRun? GetRun(string runId)
        {
            lock (_sync)
            {
                _runs.TryGetValue(runId, out var run);
                return run;
            }
        }

        public IEnumerable<WorkflowRun> Runs
        {
            get
            {
                lock (_sync)
                {
                    return _runs.Values.ToList();
                }
            }
        }
    }

    private sealed class InMemoryWorkflowRunRepository : IWorkflowRunRepository
    {
        private readonly InMemoryWorkflowRunState _state;

        public InMemoryWorkflowRunRepository(InMemoryWorkflowRunState state)
        {
            _state = state;
        }

        public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_state.GetRun(runId));
        }

        public Task<WorkflowRun> CreatePendingRunAsync(
            string runId,
            string workflowId,
            string workflowName,
            string workflowVersion,
            string? initiator,
            List<string> tags,
            string? correlationId,
            WorkflowRunCamundaLink? camunda,
            CancellationToken cancellationToken)
        {
            var run = _state.AddRun(new WorkflowRun
            {
                Id = runId,
                WorkflowId = workflowId,
                WorkflowName = workflowName,
                WorkflowVersion = workflowVersion,
                RequestedBy = initiator ?? string.Empty,
                Status = "pending",
                StartedAt = DateTimeOffset.UtcNow.ToString("o"),
                Tags = tags,
                CorrelationId = correlationId,
                CamundaProcessInstanceKey = camunda?.ProcessInstanceKey,
                CamundaProcessDefinitionKey = camunda?.ProcessDefinitionKey,
                CamundaProcessDefinitionId = camunda?.ProcessDefinitionId,
                CamundaProcessDefinitionVersion = camunda?.ProcessDefinitionVersion
            });

            return Task.FromResult(run);
        }

        public Task UpdateRunStatusAsync(string runId, string status, CancellationToken cancellationToken)
        {
            var run = _state.GetRun(runId)
                ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
            run.Status = status;
            return Task.CompletedTask;
        }

        public Task UpdateCurrentStepAsync(string runId, string? currentStep, CancellationToken cancellationToken)
        {
            var run = _state.GetRun(runId)
                ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
            run.CurrentStep = currentStep ?? string.Empty;
            return Task.CompletedTask;
        }

        public Task IncrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken)
        {
            var run = _state.GetRun(runId)
                ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
            run.PendingApprovals++;
            return Task.CompletedTask;
        }

        public Task DecrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken)
        {
            var run = _state.GetRun(runId)
                ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
            run.PendingApprovals = Math.Max(0, run.PendingApprovals - 1);
            return Task.CompletedTask;
        }

        public Task AppendEventAsync(string runId, string type, string message, CancellationToken cancellationToken)
        {
            var run = _state.GetRun(runId)
                ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
            run.Events.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid().ToString(),
                Type = type,
                Message = message,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o")
            });

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryWorkflowRuntimeStore : IWorkflowRuntimeStore
    {
        private readonly InMemoryWorkflowRunState _state;

        public InMemoryWorkflowRuntimeStore(InMemoryWorkflowRunState state)
        {
            _state = state;
        }

        public Task<WorkflowRun> CreateRunAsync(
            string workflowDefinitionId,
            string? initiator,
            CancellationToken cancellationToken,
            string? correlationId = null)
        {
            throw new NotSupportedException();
        }

        public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_state.GetRun(runId));
        }

        public Task<IReadOnlyList<WorkflowEvent>> ListRunEventsAsync(string runId, CancellationToken cancellationToken)
        {
            var run = _state.GetRun(runId);
            return Task.FromResult<IReadOnlyList<WorkflowEvent>>(run?.Events.AsReadOnly() ?? new List<WorkflowEvent>().AsReadOnly());
        }

        public Task AppendEventAsync(string runId, string type, string message, CancellationToken cancellationToken)
        {
            var run = _state.GetRun(runId)
                ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
            run.Events.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid().ToString(),
                Type = type,
                Message = message,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o")
            });

            return Task.CompletedTask;
        }

        public Task UpdateRunStatusAsync(string runId, string status, string? completedAt, CancellationToken cancellationToken)
        {
            var run = _state.GetRun(runId)
                ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
            run.Status = status;
            run.CompletedAt = completedAt;
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
            var run = _state.GetRun(runId)
                ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");

            var step = new WorkflowRunStep
            {
                Id = $"step_{Guid.NewGuid():N}",
                Name = nodeName ?? nodeId,
                Type = nodeType,
                Status = "running",
                StartedAt = DateTimeOffset.UtcNow.ToString("o"),
                AgentName = agentName
            };

            run.Steps.Add(step);
            return Task.FromResult(step);
        }

        public Task UpdateStepStatusAsync(
            string stepId,
            string status,
            string? output,
            string? error,
            string? completedAt,
            PolicyDecision? policyDecision,
            AgentRuntimeSnapshot? runtimeSnapshot,
            CancellationToken cancellationToken)
        {
            var step = _state.Runs
                .SelectMany(static run => run.Steps)
                .FirstOrDefault(step => string.Equals(step.Id, stepId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Workflow step '{stepId}' not found.");

            step.Status = status;
            step.Output = output;
            step.Error = error;
            step.CompletedAt = completedAt;
            step.PolicyDecision = policyDecision;
            step.RuntimeSnapshot = runtimeSnapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRunContextRepository : IRunContextRepository
    {
        private readonly Dictionary<string, RunContextEntry> _entries = new(StringComparer.Ordinal);

        public Task SetAsync(string runId, string key, string value, string kind, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow.ToString("o");
            _entries[$"{runId}::{key}"] = new RunContextEntry
            {
                Id = $"ctx_{Guid.NewGuid():N}",
                RunId = runId,
                Key = key,
                Value = value,
                Kind = kind,
                CreatedAt = now,
                UpdatedAt = now
            };

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RunContextEntry>> GetAllAsync(string runId, CancellationToken cancellationToken)
        {
            var entries = _entries.Values
                .Where(entry => string.Equals(entry.RunId, runId, StringComparison.Ordinal))
                .OrderBy(entry => entry.CreatedAt, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult<IReadOnlyList<RunContextEntry>>(entries);
        }
    }

    private sealed class RecordingCamundaClient : ICamundaClient
    {
        public IReadOnlyList<CamundaActivatedJob> ActivatedJobs { get; set; } = [];

        public CamundaJobActivationRequest? LastActivationRequest { get; private set; }

        public List<(string JobKey, CamundaJobCompletionRequest Request)> Completions { get; } = [];

        public Task<CamundaTopologyResponse> GetTopologyAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CamundaDeploymentResponse> DeployWorkflowAsync(
            CamundaDeploymentRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CamundaProcessStartResponse> StartProcessInstanceAsync(
            CamundaProcessStartRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<CamundaActivatedJob>> ActivateJobsAsync(
            CamundaJobActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastActivationRequest = request;
            return Task.FromResult(ActivatedJobs);
        }

        public Task CompleteJobAsync(
            string jobKey,
            CamundaJobCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Completions.Add((jobKey, request));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCamundaAgentJobExecutor : ICamundaAgentJobExecutor
    {
        public List<CamundaActivatedJob> Jobs { get; } = [];

        public Task ExecuteAsync(CamundaActivatedJob job, CancellationToken cancellationToken)
        {
            Jobs.Add(job);
            return Task.CompletedTask;
        }
    }

    private sealed class SuccessfulServiceTaskExecutor : IServiceTaskExecutor
    {
        private readonly string _output;

        public SuccessfulServiceTaskExecutor(string output)
        {
            _output = output;
        }

        public Task<AgentTaskOutcome> ExecuteAsync(
            string runId,
            string stepId,
            BpmnNodeDefinition node,
            int attempt,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: true,
                Output: _output,
                FailureReason: null));
        }
    }

    private sealed class FailingServiceTaskExecutor : IServiceTaskExecutor
    {
        private readonly string _reason;

        public FailingServiceTaskExecutor(string reason)
        {
            _reason = reason;
        }

        public Task<AgentTaskOutcome> ExecuteAsync(
            string runId,
            string stepId,
            BpmnNodeDefinition node,
            int attempt,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: _reason));
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>>;
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                values?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    ?? new Dictionary<string, object?>(StringComparer.Ordinal),
                exception));
        }

        public sealed record LogEntry(
            LogLevel LogLevel,
            string Message,
            IReadOnlyDictionary<string, object?> State,
            Exception? Exception);

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}

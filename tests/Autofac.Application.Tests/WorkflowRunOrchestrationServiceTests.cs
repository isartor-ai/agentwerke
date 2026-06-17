using System.Text.Json;
using Autofac.Application.Observability;
using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autofac.Application.Tests;

public sealed class WorkflowRunOrchestrationServiceTests
{
    [Fact]
    public async Task StartRunAsync_starts_camunda_process_and_persists_linked_run()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "wf_123",
            Name = "Invoice Approval",
            Version = "v2.0.0",
            Status = "active",
            Tags = ["finance"],
            CamundaProcessDefinitionId = "process-invoice",
            CamundaProcessDefinitionKey = "2251799813685311"
        };
        using var inputDocument = JsonDocument.Parse(
            """
            {
              "ticketId": "INC-42",
              "environment": "prod"
            }
            """);

        var definitionRepository = new StubWorkflowDefinitionRepository(workflow);
        var runRepository = new RecordingWorkflowRunRepository();
        var startService = new StubWorkflowProcessStartService(
            new WorkflowProcessStartResult(
                ProcessInstanceKey: "2251799813685401",
                ProcessDefinitionKey: "2251799813685311",
                ProcessDefinitionId: "process-invoice",
                ProcessDefinitionVersion: 2));
        var service = CreateService(
            definitionRepository,
            runRepository,
            startService,
            correlationId: "corr-123");

        var result = await service.StartRunAsync(
            new StartRunCommand(
                WorkflowId: "wf_123",
                Initiator: "api",
                Input: inputDocument.RootElement.Clone()));

        Assert.StartsWith("run_", result.RunId, StringComparison.Ordinal);
        Assert.Equal("wf_123", result.WorkflowId);
        Assert.Equal("pending", result.Status);

        Assert.Equal(1, startService.CallCount);
        Assert.Equal("2251799813685311", startService.LastRequest?.ProcessDefinitionKey);

        var variables = Assert.IsType<JsonElement>(startService.LastRequest?.Variables);
        Assert.Equal(JsonValueKind.Object, variables.ValueKind);
        Assert.Equal(result.RunId, variables.GetProperty("autofac").GetProperty("runId").GetString());
        Assert.Equal("wf_123", variables.GetProperty("autofac").GetProperty("workflowId").GetString());
        Assert.Equal("v2.0.0", variables.GetProperty("autofac").GetProperty("workflowVersion").GetString());
        Assert.Equal("api", variables.GetProperty("autofac").GetProperty("initiator").GetString());
        Assert.Equal("corr-123", variables.GetProperty("autofac").GetProperty("correlationId").GetString());
        Assert.Equal("INC-42", variables.GetProperty("input").GetProperty("ticketId").GetString());
        Assert.Equal("prod", variables.GetProperty("input").GetProperty("environment").GetString());

        var run = Assert.Single(runRepository.CreatedRuns);
        Assert.Equal(result.RunId, run.Id);
        Assert.Equal("2251799813685401", run.CamundaProcessInstanceKey);
        Assert.Equal("2251799813685311", run.CamundaProcessDefinitionKey);
        Assert.Equal("process-invoice", run.CamundaProcessDefinitionId);
        Assert.Equal(2, run.CamundaProcessDefinitionVersion);
        Assert.Equal("corr-123", run.CorrelationId);
        Assert.Equal("api", run.RequestedBy);
    }

    [Fact]
    public async Task StartRunAsync_rejects_non_object_input_before_calling_camunda()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "wf_123",
            Name = "Invoice Approval",
            Version = "v2.0.0",
            Status = "active",
            CamundaProcessDefinitionId = "process-invoice",
            CamundaProcessDefinitionKey = "2251799813685311"
        };
        using var inputDocument = JsonDocument.Parse("""["bad","input"]""");

        var runRepository = new RecordingWorkflowRunRepository();
        var startService = new StubWorkflowProcessStartService(
            new WorkflowProcessStartResult(
                ProcessInstanceKey: "2251799813685401",
                ProcessDefinitionKey: "2251799813685311",
                ProcessDefinitionId: "process-invoice",
                ProcessDefinitionVersion: 2));
        var service = CreateService(
            new StubWorkflowDefinitionRepository(workflow),
            runRepository,
            startService);

        var exception = await Assert.ThrowsAsync<WorkflowRunStartException>(() =>
            service.StartRunAsync(
                new StartRunCommand(
                    WorkflowId: "wf_123",
                    Initiator: "api",
                    Input: inputDocument.RootElement.Clone())));

        Assert.Equal("Run start failed.", exception.Message);
        Assert.Single(exception.Errors);
        Assert.Equal("invalid_input", exception.Errors[0].Code);
        Assert.Equal(0, startService.CallCount);
        Assert.Empty(runRepository.CreatedRuns);
    }

    private static WorkflowRunOrchestrationService CreateService(
        IWorkflowDefinitionRepository definitionRepository,
        RecordingWorkflowRunRepository runRepository,
        IWorkflowProcessStartService startService,
        string correlationId = "corr-default")
    {
        return new WorkflowRunOrchestrationService(
            definitionRepository,
            runRepository,
            new StubRunContextRepository(),
            new StubApprovalRepository(),
            new StubAuditRepository(),
            new StubRunOutbox(),
            new StubCorrelationContext(correlationId),
            new StubWorkflowMetrics(),
            startService,
            NullLogger<WorkflowRunOrchestrationService>.Instance);
    }

    private sealed class StubWorkflowDefinitionRepository : IWorkflowDefinitionRepository
    {
        private readonly WorkflowDefinition? _workflow;

        public StubWorkflowDefinitionRepository(WorkflowDefinition? workflow)
        {
            _workflow = workflow;
        }

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkflowDefinition>>(_workflow is null ? [] : [_workflow]);

        public Task<WorkflowDefinition?> GetAsync(string workflowId, CancellationToken cancellationToken = default)
            => Task.FromResult(
                _workflow is not null && string.Equals(_workflow.Id, workflowId, StringComparison.Ordinal)
                    ? _workflow
                    : null);

        public Task<WorkflowDefinition?> FindTrackedAsync(string workflowId, CancellationToken cancellationToken = default)
            => GetAsync(workflowId, cancellationToken);

        public Task AddAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingWorkflowRunRepository : IWorkflowRunRepository
    {
        public List<WorkflowRun> CreatedRuns { get; } = new();

        public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
            => Task.FromResult<WorkflowRun?>(CreatedRuns.FirstOrDefault(run => run.Id == runId));

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
            var run = new WorkflowRun
            {
                Id = runId,
                WorkflowId = workflowId,
                WorkflowName = workflowName,
                WorkflowVersion = workflowVersion,
                RequestedBy = initiator ?? string.Empty,
                Status = "pending",
                StartedAt = "2026-06-17T08:00:00.0000000Z",
                Tags = [.. tags],
                CorrelationId = correlationId,
                CamundaProcessInstanceKey = camunda?.ProcessInstanceKey,
                CamundaProcessDefinitionKey = camunda?.ProcessDefinitionKey,
                CamundaProcessDefinitionId = camunda?.ProcessDefinitionId,
                CamundaProcessDefinitionVersion = camunda?.ProcessDefinitionVersion
            };

            CreatedRuns.Add(run);
            return Task.FromResult(run);
        }

        public Task UpdateRunStatusAsync(string runId, string status, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UpdateCurrentStepAsync(string runId, string? currentStep, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task IncrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DecrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AppendEventAsync(string runId, string type, string message, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubWorkflowProcessStartService : IWorkflowProcessStartService
    {
        private readonly WorkflowProcessStartResult _result;

        public StubWorkflowProcessStartService(WorkflowProcessStartResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public WorkflowProcessStartRequest? LastRequest { get; private set; }

        public Task<WorkflowProcessStartResult> StartAsync(
            WorkflowProcessStartRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubRunContextRepository : IRunContextRepository
    {
        public Task SetAsync(string runId, string key, string value, string kind, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<RunContextEntry>> GetAllAsync(string runId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RunContextEntry>>([]);
    }

    private sealed class StubApprovalRepository : IApprovalRepository
    {
        public Task<ApprovalRequest?> GetApprovalAsync(string approvalId, CancellationToken cancellationToken)
            => Task.FromResult<ApprovalRequest?>(null);

        public Task<ApprovalRequest?> GetPendingApprovalForRunAsync(string runId, CancellationToken cancellationToken)
            => Task.FromResult<ApprovalRequest?>(null);

        public Task AddApprovalAsync(ApprovalRequest approval, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubAuditRepository : IAuditRepository
    {
        public Task AddAsync(AuditRecord record, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubRunOutbox : IRunOutbox
    {
        public Task EnqueueAsync(string operation, string runId, string? payload = null, DateTimeOffset? visibleAfter = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<OutboxEntry?> TryClaimNextAsync(string workerId, CancellationToken ct = default)
            => Task.FromResult<OutboxEntry?>(null);

        public Task MarkCompletedAsync(string entryId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task MarkFailedAsync(string entryId, string error, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListStuckRunIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public StubCorrelationContext(string correlationId)
        {
            CorrelationId = correlationId;
        }

        public string CorrelationId { get; }
    }

    private sealed class StubWorkflowMetrics : IWorkflowMetrics
    {
        public void RunStarted(string workflowId, string workflowName)
        {
        }

        public void RunCompleted(string workflowId, string workflowName, double durationMs)
        {
        }

        public void RunFailed(string workflowId, string workflowName, string reason)
        {
        }

        public void StepCompleted(string stepType, string agentName, double durationMs, bool succeeded)
        {
        }

        public void ApprovalCreated(string riskLevel)
        {
        }

        public void ApprovalDecided(string decision, string riskLevel)
        {
        }

        public void WebhookReceived(string source, bool triggered)
        {
        }

        public void ConnectorInvoked(string connectorId, string operation, double durationMs, bool succeeded)
        {
        }
    }
}

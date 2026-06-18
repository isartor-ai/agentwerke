using Autofac.Application.Observability;
using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autofac.Application.Tests;

public sealed class WorkflowRunOrchestrationServiceTests
{
    [Fact]
    public async Task ResumeRunAsync_RecordsApprovalDecisionUnderAuthenticatedPrincipal()
    {
        var approval = new ApprovalRequest
        {
            Id = "approval_1",
            RunId = "run_1",
            Status = "pending",
            RiskLevel = "high"
        };
        var approvalRepository = new InMemoryApprovalRepository(approval);
        var auditRepository = new CapturingAuditRepository();
        var outbox = new CapturingRunOutbox();
        var service = new WorkflowRunOrchestrationService(
            new UnusedWorkflowDefinitionRepository(),
            new InMemoryWorkflowRunRepository(new WorkflowRun { Id = "run_1", Status = "waiting" }),
            new NoOpRunContextRepository(),
            approvalRepository,
            auditRepository,
            outbox,
            new StubCorrelationContext("corr_1"),
            new NoOpWorkflowMetrics(),
            NullLogger<WorkflowRunOrchestrationService>.Instance);

        var result = await service.ResumeRunAsync(new ResumeRunCommand(
            RunId: "run_1",
            ApprovalId: "approval_1",
            Decision: "approve",
            Comment: "Ship it.",
            DecidedBy: "entra-user-42"));

        Assert.Equal("pending", result.Status);
        Assert.Equal("approved", approval.Status);
        Assert.Equal("entra-user-42", approval.DecidedBy);
        Assert.Single(auditRepository.Records);
        Assert.Equal("user", auditRepository.Records[0].ActorType);
        Assert.Equal("entra-user-42", auditRepository.Records[0].Actor);
        Assert.Equal("approval.approve", auditRepository.Records[0].Action);
        Assert.Single(outbox.Enqueued);
        Assert.Equal("resume", outbox.Enqueued[0].Operation);
        Assert.Equal("entra-user-42", OutboxResumePayload.Deserialize(outbox.Enqueued[0].Payload)?.ApprovedBy);
    }

    private sealed class UnusedWorkflowDefinitionRepository : IWorkflowDefinitionRepository
    {
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<WorkflowDefinition?> GetAsync(string workflowId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<WorkflowDefinition?> FindTrackedAsync(string workflowId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task AddAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class InMemoryWorkflowRunRepository : IWorkflowRunRepository
    {
        private readonly WorkflowRun _run;

        public InMemoryWorkflowRunRepository(WorkflowRun run)
        {
            _run = run;
        }

        public int PendingApprovalDecrements { get; private set; }

        public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Equals(_run.Id, runId, StringComparison.Ordinal) ? _run : null);
        }

        public Task<WorkflowRun> CreatePendingRunAsync(
            string runId,
            string workflowId,
            string workflowName,
            string workflowVersion,
            string? initiator,
            List<string> tags,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task UpdateRunStatusAsync(string runId, string status, CancellationToken cancellationToken)
        {
            _run.Status = status;
            return Task.CompletedTask;
        }

        public Task UpdateCurrentStepAsync(string runId, string? currentStep, CancellationToken cancellationToken)
        {
            _run.CurrentStep = currentStep ?? string.Empty;
            return Task.CompletedTask;
        }

        public Task IncrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task DecrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken)
        {
            PendingApprovalDecrements++;
            return Task.CompletedTask;
        }

        public Task AppendEventAsync(string runId, string type, string message, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class NoOpRunContextRepository : IRunContextRepository
    {
        public Task SetAsync(
            string runId,
            string key,
            string value,
            string kind,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RunContextEntry>> GetAllAsync(
            string runId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RunContextEntry>>([]);
        }
    }

    private sealed class InMemoryApprovalRepository : IApprovalRepository
    {
        private readonly ApprovalRequest _approval;

        public InMemoryApprovalRepository(ApprovalRequest approval)
        {
            _approval = approval;
        }

        public Task<ApprovalRequest?> GetApprovalAsync(string approvalId, CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Equals(_approval.Id, approvalId, StringComparison.Ordinal) ? _approval : null);
        }

        public Task<ApprovalRequest?> GetPendingApprovalForRunAsync(string runId, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                string.Equals(_approval.RunId, runId, StringComparison.Ordinal) &&
                string.Equals(_approval.Status, "pending", StringComparison.Ordinal)
                    ? _approval
                    : null);
        }

        public Task AddApprovalAsync(ApprovalRequest approval, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingAuditRepository : IAuditRepository
    {
        public List<AuditRecord> Records { get; } = [];

        public Task AddAsync(AuditRecord record, CancellationToken cancellationToken)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingRunOutbox : IRunOutbox
    {
        public List<(string Operation, string RunId, string? Payload)> Enqueued { get; } = [];

        public Task EnqueueAsync(
            string operation,
            string runId,
            string? payload = null,
            DateTimeOffset? visibleAfter = null,
            CancellationToken ct = default)
        {
            Enqueued.Add((operation, runId, payload));
            return Task.CompletedTask;
        }

        public Task<OutboxEntry?> TryClaimNextAsync(string workerId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task MarkCompletedAsync(string entryId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task MarkFailedAsync(string entryId, string error, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<string>> ListStuckRunIdsAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public StubCorrelationContext(string correlationId)
        {
            CorrelationId = correlationId;
        }

        public string CorrelationId { get; }
    }

    private sealed class NoOpWorkflowMetrics : IWorkflowMetrics
    {
        public void RunStarted(string workflowId, string workflowName) { }
        public void RunCompleted(string workflowId, string workflowName, double durationMs) { }
        public void RunFailed(string workflowId, string workflowName, string reason) { }
        public void StepCompleted(string stepType, string agentName, double durationMs, bool succeeded) { }
        public void ApprovalCreated(string riskLevel) { }
        public void ApprovalDecided(string decision, string riskLevel) { }
        public void WebhookReceived(string source, bool triggered) { }
        public void ConnectorInvoked(string connectorId, string operation, double durationMs, bool succeeded) { }
        public void ModelInvoked(string agentName, string modelId, int inputTokens, int outputTokens, double latencyMs, double costUsd, bool succeeded) { }
        public void ToolPolicyDenied(string agentName, string policyTag, string kind) { }
    }
}

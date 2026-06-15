using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace Autofac.Infrastructure.Workers;

public sealed class WorkflowRunExecutor : IWorkflowRunExecutor
{
    private readonly IWorkflowRunner _runner;
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowRunRepository _runRepository;
    private readonly IApprovalRepository _approvalRepository;
    private readonly IRunOutbox _outbox;
    private readonly ILogger<WorkflowRunExecutor> _logger;

    public WorkflowRunExecutor(
        IWorkflowRunner runner,
        IWorkflowDefinitionRepository definitionRepository,
        IWorkflowRunRepository runRepository,
        IApprovalRepository approvalRepository,
        IRunOutbox outbox,
        ILogger<WorkflowRunExecutor> logger)
    {
        _runner = runner;
        _definitionRepository = definitionRepository;
        _runRepository = runRepository;
        _approvalRepository = approvalRepository;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task ExecuteStartAsync(
        string runId,
        string workflowId,
        string? initiator,
        string? correlationId,
        CancellationToken ct)
    {
        var run = await _runRepository.GetRunAsync(runId, ct)
            ?? throw new InvalidOperationException($"Run '{runId}' not found.");

        var bpmnXml = await GetBpmnXmlAsync(run.WorkflowId, ct);

        _logger.LogInformation("Executing start for run {RunId} workflow {WorkflowId}", runId, run.WorkflowId);

        var result = await _runner.StartAsync(
            run.WorkflowId, bpmnXml, run.RequestedBy, ct, correlationId, existingRunId: runId);

        await HandleResultAsync(runId, result, ct);
    }

    public async Task ExecuteResumeAsync(string runId, string? approvedBy, CancellationToken ct)
    {
        var run = await _runRepository.GetRunAsync(runId, ct)
            ?? throw new InvalidOperationException($"Run '{runId}' not found.");

        var bpmnXml = await GetBpmnXmlAsync(run.WorkflowId, ct);

        _logger.LogInformation("Executing resume for run {RunId}", runId);

        var result = await _runner.ResumeAsync(runId, bpmnXml, approvedBy, ct);

        await HandleResultAsync(runId, result, ct);
    }

    public async Task ExecuteRecoverAsync(string runId, CancellationToken ct)
    {
        var run = await _runRepository.GetRunAsync(runId, ct)
            ?? throw new InvalidOperationException($"Run '{runId}' not found.");

        var bpmnXml = await GetBpmnXmlAsync(run.WorkflowId, ct);

        _logger.LogInformation("Executing recovery for run {RunId}", runId);

        var result = await _runner.RecoverAsync(runId, bpmnXml, ct);

        await HandleResultAsync(runId, result, ct);
    }

    private async Task HandleResultAsync(string runId, WorkflowRunnerResult result, CancellationToken ct)
    {
        switch (result.Status)
        {
            case "waiting_user":
                if (result.WaitingApproval is not null)
                    await CreateApprovalRequestAsync(runId, result.WaitingApproval, ct);
                await _runRepository.UpdateRunStatusAsync(runId, "waiting_user", ct);
                break;

            case "waiting_timer":
                if (result.TimerDueAt.HasValue)
                    await _outbox.EnqueueAsync(OutboxOperations.Timer, runId, visibleAfter: result.TimerDueAt, ct: ct);
                await _runRepository.UpdateRunStatusAsync(runId, "waiting_timer", ct);
                break;

            case "completed":
                await _runRepository.UpdateRunStatusAsync(runId, "completed", ct);
                break;

            case "failed":
                await _runRepository.UpdateRunStatusAsync(runId, "failed", ct);
                break;

            default:
                _logger.LogWarning(
                    "Unexpected execution result status '{Status}' for run {RunId}", result.Status, runId);
                break;
        }
    }

    private async Task CreateApprovalRequestAsync(
        string runId,
        WaitingApprovalInfo info,
        CancellationToken ct)
    {
        var existing = await _approvalRepository.GetPendingApprovalForRunAsync(runId, ct);
        if (existing is not null)
            return;

        var run = await _runRepository.GetRunAsync(runId, ct);

        var approval = new ApprovalRequest
        {
            Id = $"apr_{Guid.NewGuid():N}",
            RunId = runId,
            WorkflowName = run?.WorkflowName ?? runId,
            ActionRequested = info.PurposeType,
            Requester = run?.RequestedBy ?? "system",
            AgentName = info.AgentName,
            PolicyRationale = info.PolicyTag,
            RiskLevel = info.RiskLevel,
            RiskScore = info.RiskScore,
            RiskFactors = info.RiskFactors?.ToList() ?? [],
            AffectedSystems = info.AffectedSystems?.ToList() ?? [],
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };

        await _approvalRepository.AddApprovalAsync(approval, ct);
        await _approvalRepository.SaveChangesAsync(ct);
        await _runRepository.IncrementPendingApprovalsAsync(runId, ct);
    }

    private async Task<string> GetBpmnXmlAsync(string workflowId, CancellationToken ct)
    {
        var definition = await _definitionRepository.GetAsync(workflowId, ct)
            ?? throw new InvalidOperationException($"Workflow definition '{workflowId}' not found.");

        return definition.BpmnXml
            ?? throw new InvalidOperationException($"Workflow '{workflowId}' has no BPMN XML.");
    }
}

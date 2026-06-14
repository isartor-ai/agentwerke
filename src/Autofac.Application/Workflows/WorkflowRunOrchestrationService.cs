using Autofac.Application.Observability;
using Autofac.Domain.Persistence;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Autofac.Application.Workflows;

public sealed class WorkflowRunOrchestrationService : IWorkflowRunOrchestrationService
{
    private const string ActiveStatus = "active";
    private const string PendingApprovalStatus = "pending";
    private const string CancelledStatus = "cancelled";

    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowRunner _runner;
    private readonly IWorkflowRunRepository _runRepository;
    private readonly IApprovalRepository _approvalRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly ICorrelationContext _correlationContext;
    private readonly IWorkflowMetrics _metrics;
    private readonly ILogger<WorkflowRunOrchestrationService> _logger;

    public WorkflowRunOrchestrationService(
        IWorkflowDefinitionRepository definitionRepository,
        IWorkflowRunner runner,
        IWorkflowRunRepository runRepository,
        IApprovalRepository approvalRepository,
        IAuditRepository auditRepository,
        ICorrelationContext correlationContext,
        IWorkflowMetrics metrics,
        ILogger<WorkflowRunOrchestrationService> logger)
    {
        _definitionRepository = definitionRepository;
        _runner = runner;
        _runRepository = runRepository;
        _approvalRepository = approvalRepository;
        _auditRepository = auditRepository;
        _correlationContext = correlationContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<StartRunResult> StartRunAsync(
        StartRunCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var correlationId = _correlationContext.CorrelationId;

        var workflow = await _definitionRepository.GetAsync(command.WorkflowId, cancellationToken)
            ?? throw new WorkflowNotFoundException(command.WorkflowId);

        if (!string.Equals(workflow.Status, ActiveStatus, StringComparison.Ordinal))
        {
            throw new WorkflowNotPublishedException(command.WorkflowId, workflow.Status);
        }

        _logger.LogInformation(
            "Starting workflow run. WorkflowId={WorkflowId} Initiator={Initiator} CorrelationId={CorrelationId}",
            command.WorkflowId, command.Initiator, correlationId);

        _metrics.RunStarted(command.WorkflowId, workflow.Name);
        var sw = Stopwatch.StartNew();

        var result = await _runner.StartAsync(
            command.WorkflowId,
            workflow.BpmnXml,
            command.Initiator,
            cancellationToken,
            correlationId);

        if (command.Trigger is not null)
        {
            var t = command.Trigger;
            var msg = System.Text.Json.JsonSerializer.Serialize(new
            {
                source = t.Source,
                eventType = t.EventType,
                externalId = t.ExternalId,
                externalUrl = t.ExternalUrl,
                title = t.Title
            });
            await _runRepository.AppendEventAsync(result.RunId, "trigger_fired", msg, cancellationToken);
        }

        sw.Stop();
        _metrics.RunCompleted(command.WorkflowId, workflow.Name, sw.Elapsed.TotalMilliseconds);

        if (result.WaitingApproval is not null)
        {
            await _runRepository.UpdateCurrentStepAsync(result.RunId, result.WaitingApproval.NodeName ?? result.WaitingApproval.NodeId, cancellationToken);
            await _runRepository.IncrementPendingApprovalsAsync(result.RunId, cancellationToken);
            await CreateApprovalRequestAsync(
                result.RunId,
                workflow.Name,
                command.Initiator ?? "unknown",
                result.WaitingApproval,
                correlationId,
                cancellationToken);
            _metrics.ApprovalCreated(result.WaitingApproval.RiskLevel);
        }

        await WriteAuditAsync(
            runId: result.RunId,
            correlationId: correlationId,
            actorType: "system",
            actor: command.Initiator ?? "unknown",
            action: "workflow.start",
            resourceType: "workflow",
            resourceId: command.WorkflowId,
            outcome: "success",
            details: null,
            cancellationToken);

        return new StartRunResult(
            result.RunId,
            command.WorkflowId,
            result.Status,
            result.WaitingApproval);
    }

    public async Task<ResumeRunResult> ResumeRunAsync(
        ResumeRunCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var correlationId = _correlationContext.CorrelationId;

        var approval = await _approvalRepository.GetApprovalAsync(command.ApprovalId, cancellationToken)
            ?? throw new ApprovalNotFoundException(command.ApprovalId);

        if (!string.Equals(approval.Status, PendingApprovalStatus, StringComparison.Ordinal))
        {
            throw new ApprovalNotPendingException(command.ApprovalId, approval.Status);
        }

        var run = await _runRepository.GetRunAsync(command.RunId, cancellationToken)
            ?? throw new WorkflowRunNotFoundException(command.RunId);

        var workflow = await _definitionRepository.GetAsync(run.WorkflowId, cancellationToken)
            ?? throw new WorkflowNotFoundException(run.WorkflowId);

        var resolvedStatus = command.Decision switch
        {
            "approve" => "approved",
            "reject" => "rejected",
            "escalate" => "escalated",
            _ => throw new ArgumentException($"Unsupported approval decision '{command.Decision}'.", nameof(command))
        };

        var decidedBy = command.DecidedBy ?? "api-user";
        approval.Status = resolvedStatus;
        approval.DecisionComment = command.Comment;
        approval.DecidedAt = DateTimeOffset.UtcNow.ToString("o");
        approval.DecidedBy = decidedBy;

        await _approvalRepository.SaveChangesAsync(cancellationToken);
        await _runRepository.DecrementPendingApprovalsAsync(command.RunId, cancellationToken);

        _logger.LogInformation(
            "Approval decision recorded. RunId={RunId} ApprovalId={ApprovalId} Decision={Decision} DecidedBy={DecidedBy} CorrelationId={CorrelationId}",
            command.RunId, command.ApprovalId, command.Decision, decidedBy, correlationId);

        _metrics.ApprovalDecided(command.Decision, approval.RiskLevel ?? "low");

        await WriteAuditAsync(
            runId: command.RunId,
            correlationId: correlationId,
            actorType: "user",
            actor: decidedBy,
            action: $"approval.{command.Decision}",
            resourceType: "approval",
            resourceId: command.ApprovalId,
            outcome: resolvedStatus,
            details: command.Comment,
            cancellationToken);

        if (!string.Equals(resolvedStatus, "approved", StringComparison.Ordinal))
        {
            await _runRepository.UpdateRunStatusAsync(command.RunId, CancelledStatus, cancellationToken);
            await _runRepository.UpdateCurrentStepAsync(command.RunId, null, cancellationToken);
            return new ResumeRunResult(command.RunId, CancelledStatus, WaitingApproval: null);
        }

        var result = await _runner.ResumeAsync(
            command.RunId,
            workflow.BpmnXml,
            decidedBy,
            cancellationToken);

        if (result.WaitingApproval is not null)
        {
            await _runRepository.UpdateCurrentStepAsync(result.RunId, result.WaitingApproval.NodeName ?? result.WaitingApproval.NodeId, cancellationToken);
            await _runRepository.IncrementPendingApprovalsAsync(result.RunId, cancellationToken);
            await CreateApprovalRequestAsync(
                result.RunId,
                workflow.Name,
                run.RequestedBy,
                result.WaitingApproval,
                correlationId,
                cancellationToken);
        }
        else
        {
            await _runRepository.UpdateCurrentStepAsync(result.RunId, null, cancellationToken);
        }

        return new ResumeRunResult(result.RunId, result.Status, result.WaitingApproval);
    }

    public async Task<RecoverRunResult> RecoverRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await _runRepository.GetRunAsync(runId, cancellationToken)
            ?? throw new WorkflowRunNotFoundException(runId);

        var workflow = await _definitionRepository.GetAsync(run.WorkflowId, cancellationToken)
            ?? throw new WorkflowNotFoundException(run.WorkflowId);

        _logger.LogInformation(
            "Recovering workflow run. RunId={RunId} WorkflowId={WorkflowId}",
            runId, run.WorkflowId);

        var result = await _runner.RecoverAsync(runId, workflow.BpmnXml, cancellationToken);

        if (result.WaitingApproval is not null)
        {
            var existingApproval = await _approvalRepository.GetPendingApprovalForRunAsync(runId, cancellationToken);
            if (existingApproval is null)
            {
                await _runRepository.UpdateCurrentStepAsync(runId, result.WaitingApproval.NodeName ?? result.WaitingApproval.NodeId, cancellationToken);
                await _runRepository.IncrementPendingApprovalsAsync(runId, cancellationToken);
                await CreateApprovalRequestAsync(
                    runId,
                    workflow.Name,
                    run.RequestedBy,
                    result.WaitingApproval,
                    run.CorrelationId,
                    cancellationToken);
            }
        }

        return new RecoverRunResult(result.RunId, result.Status);
    }

    private async Task CreateApprovalRequestAsync(
        string runId,
        string workflowName,
        string requester,
        WaitingApprovalInfo waiting,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var slaDeadline = DateTimeOffset.UtcNow.AddHours(24).ToString("o");
        var approval = new ApprovalRequest
        {
            Id = $"apr_{Guid.NewGuid():N}",
            RunId = runId,
            WorkflowName = workflowName,
            ActionRequested = waiting.NodeName ?? waiting.PurposeType,
            Requester = requester,
            AgentName = waiting.AgentName ?? string.Empty,
            PolicyRationale = waiting.PolicyTag,
            RiskScore = waiting.RiskScore,
            RiskLevel = waiting.RiskLevel,
            RiskFactors = waiting.RiskFactors?.ToList() ?? [],
            AffectedSystems = waiting.AffectedSystems?.ToList() ?? [],
            SlaDeadline = slaDeadline,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            Status = PendingApprovalStatus,
            Priority = DeriveApprovalPriority(waiting.RiskLevel),
        };

        await _approvalRepository.AddApprovalAsync(approval, cancellationToken);
        await _approvalRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task WriteAuditAsync(
        string runId,
        string? correlationId,
        string actorType,
        string actor,
        string action,
        string resourceType,
        string resourceId,
        string outcome,
        string? details,
        CancellationToken cancellationToken)
    {
        var record = new AuditRecord
        {
            Id = $"aud_{Guid.NewGuid():N}",
            RunId = runId,
            CorrelationId = correlationId,
            ActorType = actorType,
            Actor = actor,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Outcome = outcome,
            Details = details,
            Timestamp = DateTimeOffset.UtcNow.ToString("o")
        };

        await _auditRepository.AddAsync(record, cancellationToken);
        await _auditRepository.SaveChangesAsync(cancellationToken);
    }

    private static string DeriveApprovalPriority(string riskLevel) => riskLevel switch
    {
        "critical" or "high" => "urgent",
        "medium" => "normal",
        _ => "low"
    };
}

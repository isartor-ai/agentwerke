using Autofac.Application.Observability;
using Autofac.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace Autofac.Application.Workflows;

public sealed class WorkflowRunOrchestrationService : IWorkflowRunOrchestrationService
{
    private const string PendingStatus = "pending";
    private const string PendingApprovalStatus = "pending";
    private const string CancelledStatus = "cancelled";

    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowRunRepository _runRepository;
    private readonly IRunContextRepository _runContextRepository;
    private readonly IApprovalRepository _approvalRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly IRunOutbox _outbox;
    private readonly ICorrelationContext _correlationContext;
    private readonly IWorkflowMetrics _metrics;
    private readonly ILogger<WorkflowRunOrchestrationService> _logger;

    public WorkflowRunOrchestrationService(
        IWorkflowDefinitionRepository definitionRepository,
        IWorkflowRunRepository runRepository,
        IRunContextRepository runContextRepository,
        IApprovalRepository approvalRepository,
        IAuditRepository auditRepository,
        IRunOutbox outbox,
        ICorrelationContext correlationContext,
        IWorkflowMetrics metrics,
        ILogger<WorkflowRunOrchestrationService> logger)
    {
        _definitionRepository = definitionRepository;
        _runRepository = runRepository;
        _runContextRepository = runContextRepository;
        _approvalRepository = approvalRepository;
        _auditRepository = auditRepository;
        _outbox = outbox;
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

        if (!string.Equals(workflow.Status, "active", StringComparison.Ordinal))
        {
            throw new WorkflowNotPublishedException(command.WorkflowId, workflow.Status);
        }

        var runId = $"run_{Guid.NewGuid():N}";

        await _runRepository.CreatePendingRunAsync(
            runId,
            workflow.Id,
            workflow.Name,
            workflow.Version,
            command.Initiator,
            workflow.Tags,
            correlationId,
            cancellationToken);

        if (command.Trigger is not null)
        {
            await _runRepository.AppendEventAsync(runId, "trigger_fired",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    source = command.Trigger.Source,
                    eventType = command.Trigger.EventType,
                    externalId = command.Trigger.ExternalId,
                    externalUrl = command.Trigger.ExternalUrl,
                    title = command.Trigger.Title
                }), cancellationToken);

            // Seed run context so the first agent (e.g. the BA) can read the
            // triggering issue's title/body. Later steps add "output.*" entries.
            await SeedTriggerContextAsync(runId, command.Trigger, cancellationToken);
        }

        await SeedInputContextAsync(runId, command.Inputs, cancellationToken);

        var payload = new OutboxStartPayload(workflow.Id, command.Initiator, correlationId).Serialize();
        await _outbox.EnqueueAsync(OutboxOperations.Start, runId, payload, ct: cancellationToken);

        _logger.LogInformation(
            "Workflow run enqueued. RunId={RunId} WorkflowId={WorkflowId} Initiator={Initiator} CorrelationId={CorrelationId}",
            runId, command.WorkflowId, command.Initiator, correlationId);

        _metrics.RunStarted(command.WorkflowId, workflow.Name);

        await WriteAuditAsync(
            runId: runId,
            correlationId: correlationId,
            actorType: "system",
            actor: command.Initiator ?? "unknown",
            action: "workflow.start",
            resourceType: "workflow",
            resourceId: command.WorkflowId,
            outcome: "enqueued",
            details: null,
            cancellationToken);

        return new StartRunResult(runId, command.WorkflowId, PendingStatus, WaitingApproval: null);
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

        var payload = new OutboxResumePayload(decidedBy).Serialize();
        await _outbox.EnqueueAsync(OutboxOperations.Resume, command.RunId, payload, ct: cancellationToken);

        return new ResumeRunResult(command.RunId, PendingStatus, WaitingApproval: null);
    }

    public async Task<ResumeExternalRunResult> ResumeExternalRunAsync(
        ResumeExternalRunCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = await _runRepository.GetRunAsync(command.RunId, cancellationToken)
            ?? throw new WorkflowRunNotFoundException(command.RunId);

        var resumedBy = command.ResumedBy ?? "api-user";
        var correlationId = _correlationContext.CorrelationId;

        var payload = new OutboxResumePayload(
            ApprovedBy: null,
            ExternalCorrelationKey: command.CorrelationKey,
            ExternalPayload: command.Payload,
            ResumedBy: resumedBy).Serialize();

        await _outbox.EnqueueAsync(OutboxOperations.Resume, command.RunId, payload, ct: cancellationToken);

        await WriteAuditAsync(
            runId: command.RunId,
            correlationId: correlationId,
            actorType: "operator",
            actor: resumedBy,
            action: "workflow.resume_external",
            resourceType: "workflow_run",
            resourceId: command.RunId,
            outcome: "enqueued",
            details: $"correlationKey={command.CorrelationKey}",
            cancellationToken);

        return new ResumeExternalRunResult(command.RunId, PendingStatus);
    }

    public async Task<RecoverRunResult> RecoverRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await _runRepository.GetRunAsync(runId, cancellationToken)
            ?? throw new WorkflowRunNotFoundException(runId);

        await _outbox.EnqueueAsync(OutboxOperations.Recover, runId, ct: cancellationToken);

        _logger.LogInformation("Workflow run recovery enqueued. RunId={RunId}", runId);

        return new RecoverRunResult(runId, run.Status);
    }

    private async Task SeedTriggerContextAsync(
        string runId,
        TriggerMetadata trigger,
        CancellationToken cancellationToken)
    {
        const string kind = RunContextKinds.Input;
        await _runContextRepository.SetAsync(runId, "input.source", trigger.Source, kind, cancellationToken);
        await _runContextRepository.SetAsync(runId, "input.event_type", trigger.EventType, kind, cancellationToken);
        await _runContextRepository.SetAsync(runId, "input.external_id", trigger.ExternalId, kind, cancellationToken);

        if (!string.IsNullOrWhiteSpace(trigger.ExternalUrl))
            await _runContextRepository.SetAsync(runId, "input.external_url", trigger.ExternalUrl, kind, cancellationToken);
        if (!string.IsNullOrWhiteSpace(trigger.Title))
            await _runContextRepository.SetAsync(runId, "input.title", trigger.Title, kind, cancellationToken);
        if (!string.IsNullOrWhiteSpace(trigger.Body))
            await _runContextRepository.SetAsync(runId, "input.body", trigger.Body, kind, cancellationToken);

        await SeedInputContextAsync(runId, trigger.Inputs, cancellationToken);
    }

    private async Task SeedInputContextAsync(
        string runId,
        IReadOnlyDictionary<string, string>? inputs,
        CancellationToken cancellationToken)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return;
        }

        foreach (var pair in inputs)
        {
            var key = BuildInputContextKey(pair.Key);
            if (key is null)
            {
                continue;
            }

            await _runContextRepository.SetAsync(runId, key, pair.Value, RunContextKinds.Input, cancellationToken);
        }
    }

    private static string? BuildInputContextKey(string rawKey)
    {
        var key = rawKey.Trim();
        if (key.Length == 0)
        {
            return null;
        }

        if (key.StartsWith("input.", StringComparison.OrdinalIgnoreCase))
        {
            key = key["input.".Length..].Trim();
            if (key.Length == 0)
            {
                return null;
            }
        }

        return $"input.{key}";
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
}

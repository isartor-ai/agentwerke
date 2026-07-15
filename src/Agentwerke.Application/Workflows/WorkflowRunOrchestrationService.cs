using Agentwerke.Application.Agents;
using Agentwerke.Application.Observability;
using Agentwerke.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace Agentwerke.Application.Workflows;

public sealed class WorkflowRunOrchestrationService : IWorkflowRunOrchestrationService
{
    private const string PendingStatus = "pending";
    private const string PendingApprovalStatus = "pending";
    private const string CancelledStatus = "cancelled";
    private const string ExpiredStatus = "expired";

    /// <summary>
    /// Run statuses past which a response can no longer resume anything. A late answer to one of
    /// these runs is refused rather than enqueued (#219).
    /// </summary>
    private static readonly HashSet<string> TerminalRunStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "completed", "failed", "cancelled" };

    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowRunRepository _runRepository;
    private readonly IRunContextRepository _runContextRepository;
    private readonly IApprovalRepository _approvalRepository;
    private readonly IAgentInteractionRepository _interactionRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly IRunOutbox _outbox;
    private readonly ICorrelationContext _correlationContext;
    private readonly IWorkflowMetrics _metrics;
    private readonly ILogger<WorkflowRunOrchestrationService> _logger;
    private readonly IAgentFeedbackStore? _feedbackStore;

    public WorkflowRunOrchestrationService(
        IWorkflowDefinitionRepository definitionRepository,
        IWorkflowRunRepository runRepository,
        IRunContextRepository runContextRepository,
        IApprovalRepository approvalRepository,
        IAgentInteractionRepository interactionRepository,
        IAuditRepository auditRepository,
        IRunOutbox outbox,
        ICorrelationContext correlationContext,
        IWorkflowMetrics metrics,
        ILogger<WorkflowRunOrchestrationService> logger,
        IAgentFeedbackStore? feedbackStore = null)
    {
        _definitionRepository = definitionRepository;
        _runRepository = runRepository;
        _runContextRepository = runContextRepository;
        _approvalRepository = approvalRepository;
        _interactionRepository = interactionRepository;
        _auditRepository = auditRepository;
        _outbox = outbox;
        _correlationContext = correlationContext;
        _metrics = metrics;
        _logger = logger;
        _feedbackStore = feedbackStore;
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

        // Capture the human decision as feedback about the agent that produced the work (#177).
        _feedbackStore?.Record(new AgentFeedback(
            AgentName: approval.AgentName,
            RunId: command.RunId,
            Kind: "approval",
            Signal: command.Decision,
            Comment: command.Comment,
            RecordedAt: DateTimeOffset.UtcNow.ToString("o")));

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

    public async Task<AnswerInteractionResult> AnswerInteractionAsync(
        AnswerInteractionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var interaction = await LoadScopedInteractionAsync(command.InteractionId, command.RunId, cancellationToken);
        await EnsureRunAcceptsResponsesAsync(command.RunId, cancellationToken);
        ValidateAnswerAgainstOptions(interaction, command.Answer);

        var answeredBy = command.AnsweredBy ?? "api-user";
        await TransitionAndResumeAsync(
            interaction: interaction,
            toStatus: AgentInteractionStatuses.Answered,
            response: command.Answer,
            actor: answeredBy,
            channel: command.Channel,
            auditAction: "interaction.answer",
            auditOutcome: "answered",
            cancellationToken: cancellationToken);

        return new AnswerInteractionResult(
            command.RunId, command.InteractionId, PendingStatus, command.Channel);
    }

    public async Task<RejectInteractionResult> RejectInteractionAsync(
        RejectInteractionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var interaction = await LoadScopedInteractionAsync(command.InteractionId, command.RunId, cancellationToken);
        await EnsureRunAcceptsResponsesAsync(command.RunId, cancellationToken);

        var rejectedBy = command.RejectedBy ?? "api-user";

        // A rejection resumes the run just like an answer does. It is not "do nothing": the step must
        // re-run so the tool can fail it with this reason rather than the model arguing past a human's no.
        await TransitionAndResumeAsync(
            interaction: interaction,
            toStatus: AgentInteractionStatuses.Rejected,
            response: command.Reason,
            actor: rejectedBy,
            channel: command.Channel,
            auditAction: "interaction.reject",
            auditOutcome: "rejected",
            cancellationToken: cancellationToken);

        return new RejectInteractionResult(
            command.RunId, command.InteractionId, PendingStatus, command.Channel);
    }

    public async Task<CancelInteractionResult> CancelInteractionAsync(
        CancelInteractionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var interaction = await _interactionRepository.GetByIdAsync(command.InteractionId, cancellationToken)
            ?? throw new InteractionNotFoundException(command.InteractionId);

        var cancelledBy = command.CancelledBy ?? "api-user";

        await TransitionAndResumeAsync(
            interaction: interaction,
            toStatus: AgentInteractionStatuses.Cancelled,
            response: command.Reason,
            actor: null,
            channel: null,
            auditAction: "interaction.cancel",
            auditOutcome: "cancelled",
            cancellationToken: cancellationToken,
            actorTypeOverride: "operator",
            auditActor: cancelledBy,
            onWon: won =>
            {
                won.CancelledBy = cancelledBy;
                won.CancelledAt = DateTimeOffset.UtcNow.ToString("o");
            });

        return new CancelInteractionResult(interaction.RunId, command.InteractionId, CancelledStatus);
    }

    public async Task<ExpireInteractionResult> ExpireInteractionAsync(
        string interactionId,
        CancellationToken cancellationToken = default)
    {
        var interaction = await _interactionRepository.GetByIdAsync(interactionId, cancellationToken)
            ?? throw new InteractionNotFoundException(interactionId);

        // default_answer resumes the step with the configured answer; fail and continue resume with
        // none, and the tool decides what that means for the step on re-run (#222).
        var response = string.Equals(
            interaction.ExpiresAction, InteractionExpiryActions.DefaultAnswer, StringComparison.Ordinal)
            ? interaction.DefaultAnswer
            : null;

        await TransitionAndResumeAsync(
            interaction: interaction,
            toStatus: AgentInteractionStatuses.Expired,
            response: response,
            actor: null,
            channel: null,
            auditAction: "interaction.expire",
            auditOutcome: "expired",
            cancellationToken: cancellationToken,
            actorTypeOverride: "system",
            auditActor: "interaction-timeout-sweeper");

        return new ExpireInteractionResult(interaction.RunId, interactionId, ExpiredStatus);
    }

    private async Task<AgentInteraction> LoadScopedInteractionAsync(
        string interactionId,
        string runId,
        CancellationToken cancellationToken)
    {
        var interaction = await _interactionRepository.GetByIdAsync(interactionId, cancellationToken)
            ?? throw new InteractionNotFoundException(interactionId);

        // Scope the interaction to its run so a mismatched runId can't answer someone else's question.
        if (!string.Equals(interaction.RunId, runId, StringComparison.Ordinal))
        {
            throw new InteractionNotFoundException(interactionId);
        }

        // Deliberately no "is it still pending?" check here. TryTransitionAsync is the single
        // authority: a pre-check would short-circuit a sequential duplicate before it was counted as
        // a lost race, so the metric would only see true concurrent losers and under-report replays.
        // It would also duplicate a decision that has to exist in the transition anyway.
        return interaction;
    }

    /// <summary>
    /// A response must not resume a run that has already finished — a Slack click landing after the
    /// run was cancelled must be refused, not acted on.
    /// </summary>
    private async Task EnsureRunAcceptsResponsesAsync(string runId, CancellationToken cancellationToken)
    {
        var run = await _runRepository.GetRunAsync(runId, cancellationToken)
            ?? throw new WorkflowRunNotFoundException(runId);

        if (TerminalRunStatuses.Contains(run.Status))
        {
            throw new RunNotAcceptingResponsesException(runId, run.Status);
        }
    }

    private static void ValidateAnswerAgainstOptions(AgentInteraction interaction, string answer)
    {
        if (interaction.Options.Count == 0)
        {
            return;
        }

        if (!interaction.Options.Contains(answer, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidInteractionAnswerException(interaction.Id, interaction.Options);
        }
    }

    /// <summary>
    /// The single place a terminal interaction transition and its run resume happen together.
    ///
    /// The ordering here is the point of #218: the outbox Resume is enqueued only for the caller that
    /// won the transition. Move the enqueue outside this guard and two channels answering at the same
    /// instant will each resume the run.
    /// </summary>
    private async Task TransitionAndResumeAsync(
        AgentInteraction interaction,
        string toStatus,
        string? response,
        string? actor,
        string? channel,
        string auditAction,
        string auditOutcome,
        CancellationToken cancellationToken,
        string actorTypeOverride = "user",
        string? auditActor = null,
        Action<AgentInteraction>? onWon = null)
    {
        var correlationId = _correlationContext.CorrelationId;

        var result = await _interactionRepository.TryTransitionAsync(
            interaction.Id, toStatus, response, actor, channel, cancellationToken);

        switch (result.Outcome)
        {
            case InteractionTransitionOutcome.NotFound:
                throw new InteractionNotFoundException(interaction.Id);

            case InteractionTransitionOutcome.AlreadyTerminal:
                _metrics.InteractionTransition(toStatus, channel ?? "none", won: false);
                _logger.LogInformation(
                    "Interaction transition lost. InteractionId={InteractionId} Attempted={Attempted} "
                    + "Actual={Actual} Channel={Channel} CorrelationId={CorrelationId}",
                    interaction.Id, toStatus, result.Interaction!.Status, channel, correlationId);
                throw new InteractionNotPendingException(
                    interaction.Id,
                    result.Interaction.Status,
                    result.Interaction.RespondedChannel,
                    result.Interaction.RespondedBy);
        }

        var won = result.Interaction!;
        if (onWon is not null)
        {
            onWon(won);
            await _interactionRepository.SaveChangesAsync(cancellationToken);
        }

        _metrics.InteractionTransition(toStatus, channel ?? "none", won: true);

        _logger.LogInformation(
            "Interaction {Outcome}. RunId={RunId} InteractionId={InteractionId} Actor={Actor} "
            + "Channel={Channel} CorrelationId={CorrelationId}",
            auditOutcome, won.RunId, interaction.Id, auditActor ?? actor, channel, correlationId);

        await WriteAuditAsync(
            runId: won.RunId,
            correlationId: correlationId,
            actorType: actorTypeOverride,
            actor: auditActor ?? actor ?? "system",
            action: auditAction,
            resourceType: "interaction",
            resourceId: interaction.Id,
            outcome: auditOutcome,
            details: channel is null ? response : $"channel={channel}; {response}",
            cancellationToken);

        // A non-blocking interaction has no parked step to wake — resuming would advance the run twice.
        if (!won.Blocking)
        {
            return;
        }

        // Re-run the suspended step (Phase 2 strategy); on re-run the tool finds this terminal state.
        var payload = new OutboxResumePayload(auditActor ?? actor).Serialize();
        await _outbox.EnqueueAsync(OutboxOperations.Resume, won.RunId, payload, ct: cancellationToken);
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

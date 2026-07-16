using Agentwerke.Domain.Persistence;
using System.Text.Json;

namespace Agentwerke.Application.Workflows;

// ── Commands & Results ────────────────────────────────────────────────────────

public sealed record StartRunCommand(
    string WorkflowId,
    string? Initiator,
    /// <summary>Optional metadata from an inbound integration trigger (Jira, GitHub, etc.).</summary>
    TriggerMetadata? Trigger = null,
    /// <summary>Optional custom run-context inputs, written as input.&lt;key&gt; before execution starts.</summary>
    IReadOnlyDictionary<string, string>? Inputs = null);

/// <summary>Source metadata recorded when a workflow is started by an external webhook.</summary>
public sealed record TriggerMetadata(
    string Source,
    string EventType,
    string ExternalId,
    string? ExternalUrl,
    string? Title,
    string? Body,
    /// <summary>Optional trigger-derived inputs, written as input.&lt;key&gt; before execution starts.</summary>
    IReadOnlyDictionary<string, string>? Inputs = null);

public sealed record StartRunResult(
    string RunId,
    string WorkflowId,
    string Status,
    WaitingApprovalInfo? WaitingApproval);

public sealed record ResumeRunCommand(
    string RunId,
    string ApprovalId,
    string Decision,
    string? Comment,
    string? DecidedBy);

public sealed record ResumeRunResult(string RunId, string Status, WaitingApprovalInfo? WaitingApproval);

public sealed record AnswerInteractionCommand(
    string RunId,
    string InteractionId,
    string Answer,
    string? AnsweredBy,
    /// <summary>Which channel supplied the answer — one of <see cref="InteractionChannels"/>.</summary>
    string Channel = InteractionChannels.Ui);

/// <summary><see cref="AcceptedChannel"/> names the channel whose response won (#219).</summary>
public sealed record AnswerInteractionResult(
    string RunId,
    string InteractionId,
    string Status,
    string? AcceptedChannel = null);

/// <summary>
/// Declines a confirmation (#219). Distinct from answering "reject": a rejection fails the step
/// rather than feeding a refusal back into the model loop.
/// </summary>
public sealed record RejectInteractionCommand(
    string RunId,
    string InteractionId,
    string Reason,
    string? RejectedBy,
    string Channel = InteractionChannels.Ui);

public sealed record RejectInteractionResult(
    string RunId,
    string InteractionId,
    string Status,
    string? AcceptedChannel = null);

/// <summary>Withdraws a pending interaction — by the originating agent or an operator (#219).</summary>
public sealed record CancelInteractionCommand(
    string InteractionId,
    string Reason,
    string? CancelledBy);

public sealed record CancelInteractionResult(string RunId, string InteractionId, string Status);

/// <summary>Expires a pending interaction whose timeout has passed. Raised by the sweeper (#221).</summary>
public sealed record ExpireInteractionResult(string RunId, string InteractionId, string Status);

public sealed record ResumeExternalRunCommand(
    string RunId,
    string CorrelationKey,
    IReadOnlyDictionary<string, string> Payload,
    string? ResumedBy);

public sealed record ResumeExternalRunResult(string RunId, string Status);

public sealed record RecoverRunResult(string RunId, string Status);

public sealed record WaitingApprovalInfo(
    string NodeId,
    string? NodeName,
    string PurposeType,
    string PolicyTag,
    string? AgentName = null,
    string RiskLevel = "low",
    int RiskScore = 0,
    IReadOnlyList<string>? RiskFactors = null,
    IReadOnlyList<string>? AffectedSystems = null,
    /// <summary>Artifact the preceding service task produced, for the approval card to render (#134).</summary>
    string? ArtifactName = null);

// ── Primary service interface ─────────────────────────────────────────────────

public interface IWorkflowRunOrchestrationService
{
    Task<StartRunResult> StartRunAsync(StartRunCommand command, CancellationToken cancellationToken = default);

    Task<ResumeRunResult> ResumeRunAsync(ResumeRunCommand command, CancellationToken cancellationToken = default);

    Task<AnswerInteractionResult> AnswerInteractionAsync(AnswerInteractionCommand command, CancellationToken cancellationToken = default);

    Task<RejectInteractionResult> RejectInteractionAsync(RejectInteractionCommand command, CancellationToken cancellationToken = default);

    Task<CancelInteractionResult> CancelInteractionAsync(CancelInteractionCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a timed-out interaction to expired and honours its <see cref="AgentInteraction.ExpiresAction"/>.
    /// Safe to call on an interaction that was answered a moment ago: it races the responder through the
    /// same single-winner transition and simply does nothing if it loses (#221).
    /// </summary>
    Task<ExpireInteractionResult> ExpireInteractionAsync(string interactionId, CancellationToken cancellationToken = default);

    Task<ResumeExternalRunResult> ResumeExternalRunAsync(ResumeExternalRunCommand command, CancellationToken cancellationToken = default);

    Task<RecoverRunResult> RecoverRunAsync(string runId, CancellationToken cancellationToken = default);
}

// ── Infrastructure ports (implemented in Agentwerke.Infrastructure) ──────────────

/// <summary>
/// Bridges BPMN parsing + engine execution for the orchestration service.
/// Implemented in Agentwerke.Infrastructure so Application stays BPMN-agnostic.
/// </summary>
public interface IWorkflowRunner
{
    Task<WorkflowRunnerResult> StartAsync(
        string workflowDefinitionId,
        string bpmnXml,
        string? initiator,
        CancellationToken cancellationToken,
        string? correlationId = null,
        string? existingRunId = null);

    Task<WorkflowRunnerResult> ResumeAsync(
        string runId,
        string bpmnXml,
        string? approvedBy,
        IReadOnlyDictionary<string, string>? externalPayload,
        string? externalCorrelationKey,
        string? resumedBy,
        CancellationToken cancellationToken);

    Task<WorkflowRunnerResult> RecoverAsync(
        string runId,
        string bpmnXml,
        CancellationToken cancellationToken);
}

public sealed record WorkflowRunnerResult(
    string RunId,
    string Status,
    WaitingApprovalInfo? WaitingApproval,
    DateTimeOffset? TimerDueAt = null,
    /// <summary>Set when <see cref="Status"/> is "waiting_external" (#137/#138).</summary>
    string? WaitingExternalCorrelationKey = null,
    string? WaitingExternalMessageName = null);

public interface IWorkflowRunRepository
{
    Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<WorkflowRun> CreatePendingRunAsync(string runId, string workflowId, string workflowName, string workflowVersion, string? initiator, List<string> tags, string? correlationId, CancellationToken cancellationToken);
    Task UpdateRunStatusAsync(string runId, string status, CancellationToken cancellationToken);
    Task UpdateCurrentStepAsync(string runId, string? currentStep, CancellationToken cancellationToken);
    Task IncrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken);
    Task DecrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken);
    Task AppendEventAsync(string runId, string type, string message, CancellationToken cancellationToken);
}

public interface IRunOutbox
{
    Task EnqueueAsync(string operation, string runId, string? payload = null, DateTimeOffset? visibleAfter = null, CancellationToken ct = default);
    Task<OutboxEntry?> TryClaimNextAsync(string workerId, CancellationToken ct = default);
    Task MarkCompletedAsync(string entryId, CancellationToken ct = default);
    Task MarkFailedAsync(string entryId, string error, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListStuckRunIdsAsync(CancellationToken ct = default);
}

public interface IWorkflowRunExecutor
{
    Task ExecuteStartAsync(string runId, string workflowId, string? initiator, string? correlationId, CancellationToken ct);
    Task ExecuteResumeAsync(
        string runId,
        string? approvedBy,
        string? externalCorrelationKey,
        IReadOnlyDictionary<string, string>? externalPayload,
        string? resumedBy,
        CancellationToken ct);
    Task ExecuteRecoverAsync(string runId, CancellationToken ct);
}

public static class OutboxOperations
{
    public const string Start = "start";
    public const string Resume = "resume";
    public const string Recover = "recover";
    public const string Timer = "timer";
}

public sealed record OutboxStartPayload(string WorkflowId, string? Initiator, string? CorrelationId)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public string Serialize() => JsonSerializer.Serialize(this, Options);
    public static OutboxStartPayload? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<OutboxStartPayload>(json, Options);
}

public sealed record OutboxResumePayload(
    string? ApprovedBy,
    string? ExternalCorrelationKey = null,
    IReadOnlyDictionary<string, string>? ExternalPayload = null,
    string? ResumedBy = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public string Serialize() => JsonSerializer.Serialize(this, Options);
    public static OutboxResumePayload? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<OutboxResumePayload>(json, Options);
}

public interface IApprovalRepository
{
    Task<ApprovalRequest?> GetApprovalAsync(string approvalId, CancellationToken cancellationToken);
    Task<ApprovalRequest?> GetPendingApprovalForRunAsync(string runId, CancellationToken cancellationToken);
    Task AddApprovalAsync(ApprovalRequest approval, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

// ── Exceptions ────────────────────────────────────────────────────────────────

public sealed class WorkflowRunNotFoundException : Exception
{
    public WorkflowRunNotFoundException(string runId)
        : base($"Workflow run '{runId}' was not found.")
    {
        RunId = runId;
    }

    public string RunId { get; }
}

public sealed class InteractionNotFoundException : Exception
{
    public InteractionNotFoundException(string interactionId)
        : base($"Agent interaction '{interactionId}' was not found.")
    {
        InteractionId = interactionId;
    }

    public string InteractionId { get; }
}

/// <summary>
/// The interaction already reached a terminal status. One exception covers duplicate, already-answered
/// (by another channel), expired and cancelled — all four are "not pending" — so
/// <see cref="Status"/> carries which one, and callers map it to 409 (#227).
/// </summary>
public sealed class InteractionNotPendingException : Exception
{
    public InteractionNotPendingException(string interactionId, string status)
        : base($"Agent interaction '{interactionId}' cannot be answered because its status is '{status}'.")
    {
        InteractionId = interactionId;
        Status = status;
    }

    public InteractionNotPendingException(string interactionId, string status, string? respondedChannel)
        : base(respondedChannel is null
            ? $"Agent interaction '{interactionId}' cannot be answered because its status is '{status}'."
            : $"Agent interaction '{interactionId}' was already answered via {respondedChannel}.")
    {
        InteractionId = interactionId;
        Status = status;
        RespondedChannel = respondedChannel;
    }

    public string InteractionId { get; }
    public string Status { get; }

    /// <summary>The channel whose response won, when the interaction was answered. Null otherwise.</summary>
    public string? RespondedChannel { get; }
}

/// <summary>The answer was not one of the interaction's offered choices. Maps to 400 (#227).</summary>
public sealed class InvalidInteractionAnswerException : Exception
{
    public InvalidInteractionAnswerException(string interactionId, IReadOnlyList<string> options)
        : base($"Answer must be one of: {string.Join(", ", options)}.")
    {
        InteractionId = interactionId;
        Options = options;
    }

    public string InteractionId { get; }
    public IReadOnlyList<string> Options { get; }
}

/// <summary>
/// The run reached a terminal status, so a response can no longer resume it — a Slack click that
/// lands after the run was cancelled, for example. Maps to 422 (#227).
/// </summary>
public sealed class RunNotAcceptingResponsesException : Exception
{
    public RunNotAcceptingResponsesException(string runId, string status)
        : base($"Workflow run '{runId}' is '{status}' and cannot accept interaction responses.")
    {
        RunId = runId;
        Status = status;
    }

    public string RunId { get; }
    public string Status { get; }
}

public sealed class WorkflowNotPublishedException : Exception
{
    public WorkflowNotPublishedException(string workflowId, string status)
        : base($"Workflow '{workflowId}' cannot be started because its status is '{status}'. Only active workflows can be started.")
    {
        WorkflowId = workflowId;
        Status = status;
    }

    public string WorkflowId { get; }
    public string Status { get; }
}

public sealed class ApprovalNotFoundException : Exception
{
    public ApprovalNotFoundException(string approvalId)
        : base($"Approval request '{approvalId}' was not found.")
    {
        ApprovalId = approvalId;
    }

    public string ApprovalId { get; }
}

public sealed class ApprovalNotPendingException : Exception
{
    public ApprovalNotPendingException(string approvalId, string currentStatus)
        : base($"Approval request '{approvalId}' cannot be decided because its status is '{currentStatus}'.")
    {
        ApprovalId = approvalId;
        CurrentStatus = currentStatus;
    }

    public string ApprovalId { get; }
    public string CurrentStatus { get; }
}

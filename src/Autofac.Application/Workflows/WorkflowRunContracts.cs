using Autofac.Domain.Persistence;
using System.Text.Json;

namespace Autofac.Application.Workflows;

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

    Task<ResumeExternalRunResult> ResumeExternalRunAsync(ResumeExternalRunCommand command, CancellationToken cancellationToken = default);

    Task<RecoverRunResult> RecoverRunAsync(string runId, CancellationToken cancellationToken = default);
}

// ── Infrastructure ports (implemented in Autofac.Infrastructure) ──────────────

/// <summary>
/// Bridges BPMN parsing + engine execution for the orchestration service.
/// Implemented in Autofac.Infrastructure so Application stays BPMN-agnostic.
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

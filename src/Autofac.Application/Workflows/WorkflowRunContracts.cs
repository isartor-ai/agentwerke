using Autofac.Domain.Persistence;

namespace Autofac.Application.Workflows;

// ── Commands & Results ────────────────────────────────────────────────────────

public sealed record StartRunCommand(
    string WorkflowId,
    string? Initiator,
    /// <summary>Optional metadata from an inbound integration trigger (Jira, GitHub, etc.).</summary>
    TriggerMetadata? Trigger = null);

/// <summary>Source metadata recorded when a workflow is started by an external webhook.</summary>
public sealed record TriggerMetadata(
    string Source,
    string EventType,
    string ExternalId,
    string? ExternalUrl,
    string? Title,
    string? Body);

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
    IReadOnlyList<string>? AffectedSystems = null);

// ── Primary service interface ─────────────────────────────────────────────────

public interface IWorkflowRunOrchestrationService
{
    Task<StartRunResult> StartRunAsync(StartRunCommand command, CancellationToken cancellationToken = default);

    Task<ResumeRunResult> ResumeRunAsync(ResumeRunCommand command, CancellationToken cancellationToken = default);

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
        string? correlationId = null);

    Task<WorkflowRunnerResult> ResumeAsync(
        string runId,
        string bpmnXml,
        string? approvedBy,
        CancellationToken cancellationToken);

    Task<WorkflowRunnerResult> RecoverAsync(
        string runId,
        string bpmnXml,
        CancellationToken cancellationToken);
}

public sealed record WorkflowRunnerResult(
    string RunId,
    string Status,
    WaitingApprovalInfo? WaitingApproval);

public interface IWorkflowRunRepository
{
    Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task UpdateRunStatusAsync(string runId, string status, CancellationToken cancellationToken);
    Task UpdateCurrentStepAsync(string runId, string? currentStep, CancellationToken cancellationToken);
    Task IncrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken);
    Task DecrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken);
    Task AppendEventAsync(string runId, string type, string message, CancellationToken cancellationToken);
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

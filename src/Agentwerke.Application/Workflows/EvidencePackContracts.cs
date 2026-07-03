using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;

namespace Agentwerke.Application.Workflows;

public interface IEvidencePackService
{
    Task<EvidencePack> GenerateAsync(string runId, CancellationToken cancellationToken = default);
}

public sealed record EvidencePack(
    string SchemaVersion,
    string RunId,
    string GeneratedAt,
    EvidenceWorkflow Workflow,
    EvidenceRuntime Runtime,
    EvidenceRun Run,
    IReadOnlyList<EvidenceAgentSnapshot> AgentSnapshots,
    IReadOnlyList<EvidenceApproval> Approvals,
    IReadOnlyList<EvidencePolicyDecision> PolicyDecisions,
    IReadOnlyList<EvidenceToolCall> ToolCalls,
    IReadOnlyList<EvidenceConnectorCall> ConnectorCalls,
    IReadOnlyList<EvidenceSandboxExecution> SandboxExecutions,
    IReadOnlyList<EvidenceModelUsage> ModelUsage,
    IReadOnlyList<EvidenceArtifact> Artifacts,
    IReadOnlyList<EvidenceAuditEntry> AuditLog,
    IReadOnlyList<EvidenceLogEntry> Logs,
    IReadOnlyList<EvidenceRunEvent> RunEvents,
    EvidenceCamundaMetadata? Camunda);

public sealed record EvidenceWorkflow(
    string WorkflowId,
    string Name,
    string Version,
    string? DefinitionVersion,
    string? BpmnSha256,
    string HashAlgorithm);

public sealed record EvidenceRuntime(
    string Mode,
    bool CamundaEnabled);

public sealed record EvidenceRun(
    string RunId,
    string Status,
    string RiskLevel,
    string RequestedBy,
    string StartedAt,
    string? CompletedAt,
    int? DurationMs,
    int PendingApprovals,
    string? CorrelationId,
    IReadOnlyList<string> Tags);

public sealed record EvidenceAgentSnapshot(
    string StepId,
    string StepName,
    string NodeId,
    string? AgentName,
    string? Action,
    AgentRuntimeSnapshot Snapshot);

public sealed record EvidenceApproval(
    string ApprovalId,
    string RunId,
    string ActionRequested,
    string Requester,
    string AgentName,
    string Status,
    string RiskLevel,
    int RiskScore,
    IReadOnlyList<string> RiskFactors,
    IReadOnlyList<string> AffectedSystems,
    string PolicyRationale,
    string CreatedAt,
    string? DecidedAt,
    string? DecidedBy,
    string? DecisionComment);

public sealed record EvidencePolicyDecision(
    string StepId,
    string StepName,
    string Kind,
    string? PolicyId,
    string? PolicyName,
    string? Rationale,
    int RiskScore,
    string? RiskLevel,
    IReadOnlyList<string> RiskFactors,
    string? DecidedAt,
    IReadOnlyList<string> Constraints);

public sealed record EvidenceToolCall(
    string StepId,
    string StepName,
    string? AgentName,
    string? Action,
    string ToolName,
    string Category,
    string Status,
    string? PolicyDecisionId,
    string? PolicyDecisionKind,
    string? InputSummary,
    string? OutputSummary,
    string? ErrorMessage,
    IReadOnlyList<string> ArtifactNames,
    int? DurationMs);

public sealed record EvidenceSandboxExecution(
    string StepId,
    string StepName,
    string? AgentName,
    string? Action,
    string Provider,
    string? SandboxId,
    string CommandState,
    int? ExitCode,
    int? DurationMs,
    IReadOnlyList<EvidenceSandboxLogEntry> Logs,
    IReadOnlyDictionary<string, string> Diagnostics);

public sealed record EvidenceSandboxLogEntry(
    string Stream,
    string Message,
    string Timestamp);

public sealed record EvidenceModelUsage(
    string StepId,
    string StepName,
    string? AgentName,
    string? Action,
    string? ModelId,
    int InputTokens,
    int OutputTokens,
    double? ElapsedMs);

public sealed record EvidenceConnectorCall(
    string AuditId,
    string ConnectorId,
    string Operation,
    string Actor,
    string Outcome,
    string? ResourceId,
    string? Details,
    string Timestamp,
    string? CorrelationId);

public sealed record EvidenceArtifact(
    string Source,
    string? StepId,
    string Name,
    long? SizeBytes,
    string? LastModifiedAt,
    string? Uri,
    string? ContentType);

public sealed record EvidenceAuditEntry(
    string AuditId,
    string? CorrelationId,
    string ActorType,
    string Actor,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string Outcome,
    string? Details,
    string Timestamp);

public sealed record EvidenceLogEntry(
    string Source,
    string Type,
    string Message,
    string Timestamp);

public sealed record EvidenceRunEvent(
    string EventId,
    string Type,
    string Message,
    string CreatedAt);

public sealed record EvidenceCamundaMetadata(
    string Adapter,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record EvidenceArtifactInput(
    string Name,
    long SizeBytes,
    string LastModifiedAt);

public sealed class EvidencePackNotFoundException : Exception
{
    public EvidencePackNotFoundException(string runId)
        : base($"Workflow run '{runId}' was not found.")
    {
        RunId = runId;
    }

    public string RunId { get; }
}

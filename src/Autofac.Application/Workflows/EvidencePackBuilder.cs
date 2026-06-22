using Autofac.Domain.Persistence;
using Autofac.Domain.Security;
using System.Security.Cryptography;
using System.Text;

namespace Autofac.Application.Workflows;

public static class EvidencePackBuilder
{
    public const string SchemaVersion = "autofac.evidence-pack.v1";

    public static EvidencePack Build(
        WorkflowRun run,
        WorkflowDefinition? workflow,
        IReadOnlyList<ApprovalRequest> approvals,
        IReadOnlyList<AuditRecord> auditRecords,
        IReadOnlyList<RunContextEntry> runContext,
        IReadOnlyList<EvidenceArtifactInput> storageArtifacts,
        string runtimeMode,
        bool camundaEnabled,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(run);

        var agentSnapshots = run.Steps
            .Where(step => step.RuntimeSnapshot is not null)
            .OrderBy(step => step.StartedAt ?? step.CompletedAt ?? step.Id, StringComparer.Ordinal)
            .Select(step => new EvidenceAgentSnapshot(
                StepId: step.Id,
                StepName: step.Name,
                NodeId: step.RuntimeSnapshot!.NodeId,
                AgentName: step.RuntimeSnapshot.AgentName ?? step.AgentName,
                Action: step.RuntimeSnapshot.Action,
                Snapshot: step.RuntimeSnapshot))
            .ToArray();

        var policyDecisions = run.Steps
            .Where(step => step.PolicyDecision is not null)
            .OrderBy(step => step.StartedAt ?? step.CompletedAt ?? step.Id, StringComparer.Ordinal)
            .Select(step => new EvidencePolicyDecision(
                StepId: step.Id,
                StepName: step.Name,
                Kind: step.PolicyDecision!.Kind,
                PolicyId: step.PolicyDecision.PolicyId,
                PolicyName: step.PolicyDecision.PolicyName,
                Rationale: step.PolicyDecision.Rationale,
                RiskScore: step.PolicyDecision.RiskScore,
                RiskLevel: step.PolicyDecision.RiskLevel,
                RiskFactors: step.PolicyDecision.RiskFactors.ToArray(),
                DecidedAt: step.PolicyDecision.DecidedAt,
                Constraints: step.PolicyDecision.Constraints.ToArray()))
            .ToArray();

        var toolCalls = run.Steps
            .Where(step => step.RuntimeSnapshot is not null)
            .OrderBy(step => step.StartedAt ?? step.CompletedAt ?? step.Id, StringComparer.Ordinal)
            .SelectMany(step => step.RuntimeSnapshot!.ToolInvocations.Select(tool => new EvidenceToolCall(
                StepId: step.Id,
                StepName: step.Name,
                AgentName: step.RuntimeSnapshot.AgentName ?? step.AgentName,
                Action: step.RuntimeSnapshot.Action,
                ToolName: tool.ToolName,
                Category: tool.Category,
                Status: tool.Status,
                PolicyDecisionId: tool.PolicyDecisionId,
                PolicyDecisionKind: tool.PolicyDecisionKind,
                InputSummary: RedactOptional(tool.InputSummary),
                OutputSummary: RedactOptional(tool.OutputSummary),
                ErrorMessage: RedactOptional(tool.ErrorMessage),
                ArtifactNames: tool.ArtifactNames.ToArray(),
                DurationMs: tool.DurationMs)))
            .ToArray();

        var sandboxExecutions = run.Steps
            .Where(step => step.RuntimeSnapshot?.SandboxExecution is not null)
            .OrderBy(step => step.StartedAt ?? step.CompletedAt ?? step.Id, StringComparer.Ordinal)
            .Select(step =>
            {
                var sandbox = step.RuntimeSnapshot!.SandboxExecution!;
                return new EvidenceSandboxExecution(
                    StepId: step.Id,
                    StepName: step.Name,
                    AgentName: step.RuntimeSnapshot.AgentName ?? step.AgentName,
                    Action: step.RuntimeSnapshot.Action,
                    Provider: sandbox.Provider,
                    SandboxId: sandbox.SandboxId,
                    CommandState: sandbox.CommandState,
                    ExitCode: sandbox.ExitCode,
                    DurationMs: sandbox.DurationMs,
                    Logs: sandbox.Logs
                        .Select(log => new EvidenceSandboxLogEntry(
                            log.Stream,
                            RedactOptional(log.Message) ?? string.Empty,
                            log.Timestamp))
                        .ToArray(),
                    Diagnostics: sandbox.Diagnostics.ToDictionary(
                        entry => entry.Key,
                        entry => RedactOptional(entry.Value) ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase));
            })
            .ToArray();

        var modelUsage = run.Steps
            .Where(step => step.RuntimeSnapshot?.TokenUsage is not null)
            .OrderBy(step => step.StartedAt ?? step.CompletedAt ?? step.Id, StringComparer.Ordinal)
            .Select(step =>
            {
                var usage = step.RuntimeSnapshot!.TokenUsage!;
                return new EvidenceModelUsage(
                    StepId: step.Id,
                    StepName: step.Name,
                    AgentName: step.RuntimeSnapshot.AgentName ?? step.AgentName,
                    Action: step.RuntimeSnapshot.Action,
                    ModelId: usage.ModelId,
                    InputTokens: usage.InputTokens,
                    OutputTokens: usage.OutputTokens,
                    ElapsedMs: usage.ElapsedMs);
            })
            .ToArray();

        var connectorCalls = auditRecords
            .Where(IsConnectorAudit)
            .OrderBy(record => record.Timestamp, StringComparer.Ordinal)
            .Select(ToConnectorCall)
            .ToArray();

        var storageEvidence = storageArtifacts
            .OrderBy(artifact => artifact.Name, StringComparer.OrdinalIgnoreCase)
            .Select(artifact => new EvidenceArtifact(
                Source: "artifact-storage",
                StepId: null,
                Name: artifact.Name,
                SizeBytes: artifact.SizeBytes,
                LastModifiedAt: artifact.LastModifiedAt,
                Uri: null,
                ContentType: null));

        var snapshotEvidence = run.Steps
            .Where(step => step.RuntimeSnapshot is not null)
            .OrderBy(step => step.StartedAt ?? step.CompletedAt ?? step.Id, StringComparer.Ordinal)
            .SelectMany(step => step.RuntimeSnapshot!.Artifacts.Select(artifact => new EvidenceArtifact(
                Source: "agent-runtime-snapshot",
                StepId: step.Id,
                Name: artifact.Name,
                SizeBytes: null,
                LastModifiedAt: null,
                Uri: artifact.Uri,
                ContentType: artifact.ContentType)));

        var auditLog = auditRecords
            .OrderBy(record => record.Timestamp, StringComparer.Ordinal)
            .Select(record => new EvidenceAuditEntry(
                AuditId: record.Id,
                CorrelationId: record.CorrelationId,
                ActorType: record.ActorType,
                Actor: record.Actor,
                Action: record.Action,
                ResourceType: record.ResourceType,
                ResourceId: record.ResourceId,
                Outcome: record.Outcome,
                Details: RedactOptional(record.Details),
                Timestamp: record.Timestamp))
            .ToArray();

        var runEvents = run.Events
            .OrderBy(runEvent => runEvent.CreatedAt, StringComparer.Ordinal)
            .Select(runEvent => new EvidenceRunEvent(
                EventId: runEvent.Id,
                Type: runEvent.Type,
                Message: RedactOptional(runEvent.Message) ?? string.Empty,
                CreatedAt: runEvent.CreatedAt))
            .ToArray();

        var logs = runEvents
            .Select(runEvent => new EvidenceLogEntry(
                Source: "workflow-event",
                Type: runEvent.Type,
                Message: RedactOptional(runEvent.Message) ?? string.Empty,
                Timestamp: runEvent.CreatedAt))
            .Concat(auditLog.Select(audit => new EvidenceLogEntry(
                Source: "audit",
                Type: audit.Action,
                Message: RedactOptional(audit.Details ?? $"{audit.ActorType}:{audit.Actor} {audit.Outcome}") ?? string.Empty,
                Timestamp: audit.Timestamp)))
            .OrderBy(log => log.Timestamp, StringComparer.Ordinal)
            .ToArray();

        return new EvidencePack(
            SchemaVersion: SchemaVersion,
            RunId: run.Id,
            GeneratedAt: generatedAt.ToString("o"),
            Workflow: new EvidenceWorkflow(
                WorkflowId: run.WorkflowId,
                Name: run.WorkflowName,
                Version: run.WorkflowVersion,
                DefinitionVersion: workflow?.Version,
                BpmnSha256: ComputeSha256(workflow?.BpmnXml),
                HashAlgorithm: "SHA-256"),
            Runtime: new EvidenceRuntime(runtimeMode, camundaEnabled),
            Run: new EvidenceRun(
                RunId: run.Id,
                Status: run.Status,
                RiskLevel: run.RiskLevel,
                RequestedBy: run.RequestedBy,
                StartedAt: run.StartedAt,
                CompletedAt: run.CompletedAt,
                DurationMs: run.DurationMs,
                PendingApprovals: run.PendingApprovals,
                CorrelationId: run.CorrelationId,
                Tags: run.Tags.ToArray()),
            AgentSnapshots: agentSnapshots,
            Approvals: approvals
                .OrderBy(approval => approval.CreatedAt, StringComparer.Ordinal)
                .Select(ToApproval)
                .ToArray(),
            PolicyDecisions: policyDecisions,
            ToolCalls: toolCalls,
            ConnectorCalls: connectorCalls,
            SandboxExecutions: sandboxExecutions,
            ModelUsage: modelUsage,
            Artifacts: storageEvidence.Concat(snapshotEvidence).ToArray(),
            AuditLog: auditLog,
            Logs: logs,
            RunEvents: runEvents,
            Camunda: camundaEnabled ? BuildCamundaMetadata(runContext) : null);
    }

    private static EvidenceApproval ToApproval(ApprovalRequest approval)
    {
        return new EvidenceApproval(
            ApprovalId: approval.Id,
            RunId: approval.RunId,
            ActionRequested: approval.ActionRequested,
            Requester: approval.Requester,
            AgentName: approval.AgentName,
            Status: approval.Status,
            RiskLevel: approval.RiskLevel,
            RiskScore: approval.RiskScore,
            RiskFactors: approval.RiskFactors.ToArray(),
            AffectedSystems: approval.AffectedSystems.ToArray(),
            PolicyRationale: approval.PolicyRationale,
            CreatedAt: approval.CreatedAt,
            DecidedAt: approval.DecidedAt,
            DecidedBy: approval.DecidedBy,
            DecisionComment: approval.DecisionComment);
    }

    private static bool IsConnectorAudit(AuditRecord record)
    {
        return string.Equals(record.ActorType, "connector", StringComparison.OrdinalIgnoreCase)
            || record.Action.StartsWith("connector.", StringComparison.OrdinalIgnoreCase);
    }

    private static EvidenceConnectorCall ToConnectorCall(AuditRecord record)
    {
        var parts = record.Action.Split('.', 3, StringSplitOptions.RemoveEmptyEntries);
        var connectorId = parts.Length >= 2 ? parts[1] : record.ResourceId ?? record.Actor;
        var operation = parts.Length >= 3 ? parts[2] : record.Action;

        return new EvidenceConnectorCall(
            AuditId: record.Id,
            ConnectorId: connectorId,
            Operation: operation,
            Actor: record.Actor,
            Outcome: record.Outcome,
            ResourceId: record.ResourceId,
            Details: RedactOptional(record.Details),
            Timestamp: record.Timestamp,
            CorrelationId: record.CorrelationId);
    }

    private static string? RedactOptional(string? value) =>
        value is null ? null : SecretRedactor.Redact(value);

    private static EvidenceCamundaMetadata BuildCamundaMetadata(IReadOnlyList<RunContextEntry> runContext)
    {
        var metadata = runContext
            .Where(entry =>
                entry.Key.StartsWith("camunda.", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("camunda_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        return new EvidenceCamundaMetadata("camunda", metadata);
    }

    private static string? ComputeSha256(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

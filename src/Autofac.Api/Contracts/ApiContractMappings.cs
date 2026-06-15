using System;
using System.Linq;
using Autofac.Application.Workflows;
using Autofac.Api.Contracts.Approvals;
using Autofac.Api.Contracts.Runs;
using Autofac.Api.Contracts.Workflows;
using Autofac.Domain.Persistence;
using Autofac.Storage.Artifacts;
using RunPolicyDecision = Autofac.Api.Contracts.Runs.PolicyDecision;
using DomainPolicyDecision = Autofac.Domain.Persistence.PolicyDecision;
using DomainPromptSnapshot = Autofac.Domain.AgentRuntime.AgentPromptSnapshot;

namespace Autofac.Api.Contracts;

internal static class ApiContractMappings
{
    public static WorkflowSummary ToWorkflowSummary(WorkflowDefinition workflow)
    {
        return new WorkflowSummary(
            workflow.Id,
            workflow.Name,
            workflow.Description,
            workflow.Version,
            workflow.Status,
            workflow.Owner,
            workflow.CreatedAt,
            workflow.LastEditedAt,
            workflow.ValidationState,
            workflow.Tags.ToArray());
    }

    public static WorkflowDetail ToWorkflowDetail(WorkflowDefinition workflow)
    {
        return new WorkflowDetail(
            workflow.Id,
            workflow.Name,
            workflow.Description,
            workflow.Version,
            workflow.Status,
            workflow.Owner,
            workflow.CreatedAt,
            workflow.LastEditedAt,
            workflow.ValidationState,
            workflow.Tags.ToArray(),
            workflow.BpmnXml);
    }

    public static ValidationResponse ToValidationResponse(WorkflowValidationResult validation)
    {
        return new ValidationResponse(
            validation.IsValid,
            validation.ProcessId,
            validation.ProcessName,
            validation.Errors.Select(error => new ValidationErrorResponse(
                error.Message,
                error.ElementId,
                error.ElementName,
                error.LineNumber,
                error.LinePosition)).ToArray(),
            validation.Warnings.Select(warning => new ValidationWarningResponse(
                warning.Message,
                warning.ElementId,
                warning.ElementName,
                warning.LineNumber,
                warning.LinePosition)).ToArray());
    }

    public static ImportWorkflowResponse ToImportWorkflowResponse(WorkflowImportResult result)
    {
        return new ImportWorkflowResponse(result.WorkflowId, ToValidationResponse(result.Validation));
    }

    public static PublishWorkflowResponse ToPublishWorkflowResponse(WorkflowPublishResult result)
    {
        return new PublishWorkflowResponse(result.WorkflowId, result.Version, result.PublishedAt);
    }

    public static RunSummary ToRunSummary(WorkflowRun run)
    {
        return new RunSummary(
            run.Id,
            run.WorkflowId,
            run.WorkflowName,
            run.WorkflowVersion,
            NormalizeRunStatus(run.Status),
            run.RiskLevel,
            string.IsNullOrWhiteSpace(run.CurrentStep) ? null : run.CurrentStep,
            run.RequestedBy,
            run.StartedAt,
            run.CompletedAt,
            run.DurationMs,
            run.PendingApprovals,
            run.Tags.ToArray(),
            run.Events
                .OrderBy(static item => item.CreatedAt, StringComparer.Ordinal)
                .Select(ToRunEvent)
                .ToArray());
    }

    public static RunDetail ToRunDetail(
        WorkflowRun run,
        IReadOnlyList<ApprovalRequest> approvals,
        IReadOnlyList<ArtifactDescriptor> artifacts)
    {
        return new RunDetail(
            run.Id,
            run.WorkflowId,
            run.WorkflowName,
            run.WorkflowVersion,
            NormalizeRunStatus(run.Status),
            run.RiskLevel,
            string.IsNullOrWhiteSpace(run.CurrentStep) ? null : run.CurrentStep,
            run.RequestedBy,
            run.StartedAt,
            run.CompletedAt,
            run.DurationMs,
            run.PendingApprovals,
            run.Tags.ToArray(),
            run.Events
                .OrderByDescending(static item => item.CreatedAt, StringComparer.Ordinal)
                .Select(ToRunEvent)
                .ToArray(),
            run.Steps
                .OrderBy(static item => item.StartedAt ?? item.CompletedAt ?? item.Id, StringComparer.Ordinal)
                .Select(ToRunStep)
                .ToArray(),
            artifacts.Select(ToRunArtifact).ToArray(),
            approvals.Select(ToApprovalSummary).ToArray());
    }

    public static ApprovalSummary ToApprovalSummary(ApprovalRequest approval)
    {
        return new ApprovalSummary(
            approval.Id,
            approval.RunId,
            approval.WorkflowName,
            approval.ActionRequested,
            approval.Requester,
            approval.AgentName,
            approval.PolicyRationale,
            approval.RiskScore,
            approval.RiskLevel,
            approval.RiskFactors.ToArray(),
            approval.AffectedSystems.ToArray(),
            approval.SlaDeadline,
            approval.CreatedAt,
            approval.Status,
            approval.Priority,
            approval.DecisionComment,
            approval.DecidedBy,
            approval.DecidedAt);
    }

    public static RunEvent ToRunEvent(WorkflowEvent runEvent)
    {
        return new RunEvent(
            runEvent.Id,
            runEvent.Type,
            runEvent.Message,
            runEvent.CreatedAt);
    }

    public static RunStep ToRunStep(WorkflowRunStep step)
    {
        return new RunStep(
            step.Id,
            step.Name,
            step.Type,
            NormalizeRunStatus(step.Status),
            step.StartedAt,
            step.CompletedAt,
            step.AgentName,
            step.Output,
            Error: null,
            PolicyDecision: step.PolicyDecision is null ? null : ToPolicyDecision(step.PolicyDecision),
            PromptSnapshot: step.RuntimeSnapshot?.Prompt is null ? null : ToPromptSnapshot(step.RuntimeSnapshot.Prompt),
            Skills: step.RuntimeSnapshot?.Skills.Select(static skill => new SkillAuditRecord(
                skill.SkillId,
                skill.Name,
                skill.Description,
                skill.Version,
                skill.Fingerprint,
                skill.InvocationRules.ToArray(),
                skill.RequiredFiles.ToArray(),
                skill.OptionalTools.ToArray(),
                skill.Source,
                skill.Available,
                skill.Selected,
                skill.Invoked)).ToArray() ?? [],
            ToolInvocations: step.RuntimeSnapshot?.ToolInvocations.Select(static tool => new ToolInvocationRecord(
                tool.ToolName,
                tool.Category,
                tool.Status,
                tool.PolicyDecisionId,
                tool.PolicyDecisionKind,
                tool.InputSummary,
                tool.OutputSummary,
                tool.ErrorMessage,
                tool.ArtifactNames.ToArray(),
                tool.DurationMs)).ToArray() ?? [],
            HookExecutions: step.RuntimeSnapshot?.HookExecutions.Select(static hook => new HookExecutionRecord(
                hook.HookName,
                hook.Event,
                hook.Type,
                hook.Decision,
                hook.Blocking,
                hook.OutputSummary,
                hook.ErrorMessage,
                hook.DurationMs)).ToArray() ?? [],
            SubAgentRuns: step.RuntimeSnapshot?.SubAgentRuns.Select(static child => new SubAgentRunRecord(
                child.RunId,
                child.ParentRunId,
                child.ParentStepId,
                child.AgentName,
                child.Action,
                child.Status,
                child.Depth,
                child.PermissionLevel,
                child.FailureBehavior,
                child.CorrelationId,
                child.StartedAt,
                child.CompletedAt,
                child.OutputSummary,
                child.FailureReason,
                child.ArtifactNames.ToArray(),
                child.EventMessages.ToArray())).ToArray() ?? []);
    }

    public static string NormalizeRunStatus(string status)
    {
        return string.Equals(status, "waiting_user", StringComparison.Ordinal)
            ? "awaiting_approval"
            : status;
    }

    public static ApprovalDecisionResponse ToApprovalDecisionResponse(string approvalId, ApprovalRequest approval)
    {
        return new ApprovalDecisionResponse(
            approvalId,
            approval.Status,
            approval.DecidedAt ?? string.Empty,
            approval.DecidedBy ?? string.Empty,
            approval.DecisionComment);
    }

    private static RunPolicyDecision ToPolicyDecision(DomainPolicyDecision decision)
    {
        return new RunPolicyDecision(
            decision.Kind,
            decision.PolicyId,
            decision.PolicyName,
            decision.Rationale,
            decision.RiskScore,
            decision.RiskLevel,
            decision.RiskFactors.ToArray(),
            decision.DecidedAt,
            decision.Constraints.ToArray());
    }

    private static RunArtifact ToRunArtifact(ArtifactDescriptor artifact)
    {
        return new RunArtifact(
            artifact.Name,
            artifact.SizeBytes,
            artifact.LastModifiedAt);
    }

    private static PromptSnapshot ToPromptSnapshot(DomainPromptSnapshot snapshot)
    {
        return new PromptSnapshot(
            snapshot.FinalPrompt,
            snapshot.RenderedAt,
            snapshot.Sections.Select(static section => new PromptSection(
                section.Name,
                section.Content,
                section.Source)).ToArray(),
            snapshot.Variables,
            snapshot.SourceFiles.ToArray());
    }
}

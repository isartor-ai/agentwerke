using System;
using System.Linq;
using Autofac.Application.Workflows;
using Autofac.Api.Contracts.Approvals;
using Autofac.Api.Contracts.Runs;
using Autofac.Api.Contracts.Templates;
using Autofac.Api.Contracts.Workflows;
using Autofac.Domain.AgentRuntime;
using Autofac.Domain.Persistence;
using Autofac.Storage.Artifacts;
using RunPolicyDecision = Autofac.Api.Contracts.Runs.PolicyDecision;
using DomainPolicyDecision = Autofac.Domain.Persistence.PolicyDecision;

namespace Autofac.Api.Contracts;

internal static class ApiContractMappings
{
    public static TemplateSummary ToTemplateSummary(SdlcTemplate template) =>
        new(
            template.Id,
            template.Name,
            template.Description,
            template.Trigger,
            template.PolicyLevel,
            template.Tags.ToArray(),
            template.AgentRoles.ToArray(),
            template.ApprovalRoles.ToArray());

    public static TemplateDetail ToTemplateDetail(SdlcTemplate template) =>
        new(
            template.Id,
            template.Name,
            template.Description,
            template.Trigger,
            template.PolicyLevel,
            template.Tags.ToArray(),
            template.AgentRoles.ToArray(),
            template.ApprovalRoles.ToArray(),
            template.RequiredInputs.ToArray(),
            template.EvidenceExpectations.ToArray(),
            template.BpmnXml);


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
            step.Error,
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
                hook.DurationMs)).ToArray() ?? []);
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

    private static PromptSnapshot ToPromptSnapshot(AgentPromptSnapshot prompt)
    {
        return new PromptSnapshot(
            prompt.FinalPrompt,
            prompt.RenderedAt,
            prompt.Sections.Select(static s => new PromptSection(s.Name, s.Content, s.Source)).ToArray(),
            prompt.Variables,
            prompt.SourceFiles.ToArray());
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

    private static RunStepRuntimeSnapshot ToRunStepRuntimeSnapshot(AgentRuntimeSnapshot snapshot)
    {
        var contract = snapshot.Contract;
        return new RunStepRuntimeSnapshot(
            AgentName: snapshot.AgentName,
            Action: snapshot.Action,
            PromptInline: snapshot.Prompt?.FinalPrompt ?? contract.Prompt?.Inline,
            Prompt: snapshot.Prompt is null ? null : new PromptSnapshot(
                snapshot.Prompt.FinalPrompt,
                snapshot.Prompt.RenderedAt,
                snapshot.Prompt.Sections
                    .Select(static s => new PromptSection(s.Name, s.Content, s.Source))
                    .ToArray(),
                snapshot.Prompt.Variables,
                snapshot.Prompt.SourceFiles.ToArray()),
            Skills: snapshot.Skills
                .Select(static s => new RunStepSkillUsage(s.SkillId, s.Name, s.Selected, s.Fingerprint, s.Invoked, s.Source))
                .ToArray(),
            Tools: contract.Tools
                .Select(static t => new RunStepToolInfo(t.Name, t.Category))
                .ToArray(),
            ToolInvocations: snapshot.ToolInvocations
                .Select(static t => new RunStepToolInvocation(
                    t.ToolName, t.Category, t.Status,
                    t.PolicyDecisionId, t.PolicyDecisionKind,
                    t.InputSummary, t.OutputSummary, t.ErrorMessage,
                    t.ArtifactNames.ToArray(), t.DurationMs))
                .ToArray(),
            McpServers: contract.McpServers
                .Select(static m => m.Name)
                .ToArray(),
            Hooks: snapshot.HookExecutions
                .Select(static h => new RunStepHookExecution(h.Event, h.Type, h.Decision, h.DurationMs))
                .ToArray(),
            PermissionLevel: contract.Permissions.Level,
            AllowedTools: contract.Permissions.AllowedTools.ToArray(),
            DeniedTools: contract.Permissions.DeniedTools.ToArray(),
            SubAgentsEnabled: contract.SubAgents?.Enabled ?? false,
            PermissionDecision: snapshot.PermissionDecision is null ? null
                : new RunStepPermissionDecision(
                    snapshot.PermissionDecision.Level,
                    snapshot.PermissionDecision.Allowed,
                    snapshot.PermissionDecision.Rationale),
            StepArtifacts: snapshot.Artifacts
                .Select(static a => new RunStepArtifactRef(a.Name, a.Uri, a.ContentType))
                .ToArray());
    }
}

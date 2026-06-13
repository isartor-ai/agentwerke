using System;
using System.Linq;
using Autofac.Application.Workflows;
using Autofac.Api.Contracts.Approvals;
using Autofac.Api.Contracts.Runs;
using Autofac.Api.Contracts.Workflows;
using Autofac.Domain.Persistence;
using RunPolicyDecision = Autofac.Api.Contracts.Runs.PolicyDecision;
using DomainPolicyDecision = Autofac.Domain.Persistence.PolicyDecision;

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

    public static RunDetail ToRunDetail(WorkflowRun run)
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
                .OrderBy(static item => item.CreatedAt, StringComparer.Ordinal)
                .Select(ToRunEvent)
                .ToArray(),
            run.Steps
                .OrderBy(static item => item.StartedAt ?? item.CompletedAt ?? item.Id, StringComparer.Ordinal)
                .Select(ToRunStep)
                .ToArray());
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
            PolicyDecision: step.PolicyDecision is null ? null : ToPolicyDecision(step.PolicyDecision));
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
}

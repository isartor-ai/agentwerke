using Autofac.Domain.Persistence;

namespace Autofac.Application.Workflows;

public sealed class WorkflowRunOrchestrationService : IWorkflowRunOrchestrationService
{
    private const string ActiveStatus = "active";
    private const string PendingApprovalStatus = "pending";
    private const string CancelledStatus = "cancelled";

    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowRunner _runner;
    private readonly IWorkflowRunRepository _runRepository;
    private readonly IApprovalRepository _approvalRepository;

    public WorkflowRunOrchestrationService(
        IWorkflowDefinitionRepository definitionRepository,
        IWorkflowRunner runner,
        IWorkflowRunRepository runRepository,
        IApprovalRepository approvalRepository)
    {
        _definitionRepository = definitionRepository;
        _runner = runner;
        _runRepository = runRepository;
        _approvalRepository = approvalRepository;
    }

    public async Task<StartRunResult> StartRunAsync(
        StartRunCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var workflow = await _definitionRepository.GetAsync(command.WorkflowId, cancellationToken)
            ?? throw new WorkflowNotFoundException(command.WorkflowId);

        if (!string.Equals(workflow.Status, ActiveStatus, StringComparison.Ordinal))
        {
            throw new WorkflowNotPublishedException(command.WorkflowId, workflow.Status);
        }

        var result = await _runner.StartAsync(
            command.WorkflowId,
            workflow.BpmnXml,
            command.Initiator,
            cancellationToken);

        if (command.Trigger is not null)
        {
            var t = command.Trigger;
            var msg = System.Text.Json.JsonSerializer.Serialize(new
            {
                source = t.Source,
                eventType = t.EventType,
                externalId = t.ExternalId,
                externalUrl = t.ExternalUrl,
                title = t.Title
            });
            await _runRepository.AppendEventAsync(result.RunId, "trigger_fired", msg, cancellationToken);
        }

        if (result.WaitingApproval is not null)
        {
            await _runRepository.UpdateCurrentStepAsync(result.RunId, result.WaitingApproval.NodeName ?? result.WaitingApproval.NodeId, cancellationToken);
            await _runRepository.IncrementPendingApprovalsAsync(result.RunId, cancellationToken);
            await CreateApprovalRequestAsync(
                result.RunId,
                workflow.Name,
                command.Initiator ?? "unknown",
                result.WaitingApproval,
                cancellationToken);
        }

        return new StartRunResult(
            result.RunId,
            command.WorkflowId,
            result.Status,
            result.WaitingApproval);
    }

    public async Task<ResumeRunResult> ResumeRunAsync(
        ResumeRunCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var approval = await _approvalRepository.GetApprovalAsync(command.ApprovalId, cancellationToken)
            ?? throw new ApprovalNotFoundException(command.ApprovalId);

        if (!string.Equals(approval.Status, PendingApprovalStatus, StringComparison.Ordinal))
        {
            throw new ApprovalNotPendingException(command.ApprovalId, approval.Status);
        }

        var run = await _runRepository.GetRunAsync(command.RunId, cancellationToken)
            ?? throw new WorkflowRunNotFoundException(command.RunId);

        var workflow = await _definitionRepository.GetAsync(run.WorkflowId, cancellationToken)
            ?? throw new WorkflowNotFoundException(run.WorkflowId);

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

        if (!string.Equals(resolvedStatus, "approved", StringComparison.Ordinal))
        {
            await _runRepository.UpdateRunStatusAsync(command.RunId, CancelledStatus, cancellationToken);
            await _runRepository.UpdateCurrentStepAsync(command.RunId, null, cancellationToken);
            return new ResumeRunResult(command.RunId, CancelledStatus, WaitingApproval: null);
        }

        var result = await _runner.ResumeAsync(
            command.RunId,
            workflow.BpmnXml,
            decidedBy,
            cancellationToken);

        if (result.WaitingApproval is not null)
        {
            await _runRepository.UpdateCurrentStepAsync(result.RunId, result.WaitingApproval.NodeName ?? result.WaitingApproval.NodeId, cancellationToken);
            await _runRepository.IncrementPendingApprovalsAsync(result.RunId, cancellationToken);
            await CreateApprovalRequestAsync(
                result.RunId,
                workflow.Name,
                run.RequestedBy,
                result.WaitingApproval,
                cancellationToken);
        }
        else
        {
            await _runRepository.UpdateCurrentStepAsync(result.RunId, null, cancellationToken);
        }

        return new ResumeRunResult(result.RunId, result.Status, result.WaitingApproval);
    }

    public async Task<RecoverRunResult> RecoverRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await _runRepository.GetRunAsync(runId, cancellationToken)
            ?? throw new WorkflowRunNotFoundException(runId);

        var workflow = await _definitionRepository.GetAsync(run.WorkflowId, cancellationToken)
            ?? throw new WorkflowNotFoundException(run.WorkflowId);

        var result = await _runner.RecoverAsync(runId, workflow.BpmnXml, cancellationToken);

        if (result.WaitingApproval is not null)
        {
            var existingApproval = await _approvalRepository.GetPendingApprovalForRunAsync(runId, cancellationToken);
            if (existingApproval is null)
            {
                await _runRepository.UpdateCurrentStepAsync(runId, result.WaitingApproval.NodeName ?? result.WaitingApproval.NodeId, cancellationToken);
                await _runRepository.IncrementPendingApprovalsAsync(runId, cancellationToken);
                await CreateApprovalRequestAsync(
                    runId,
                    workflow.Name,
                    run.RequestedBy,
                    result.WaitingApproval,
                    cancellationToken);
            }
        }

        return new RecoverRunResult(result.RunId, result.Status);
    }

    private async Task CreateApprovalRequestAsync(
        string runId,
        string workflowName,
        string requester,
        WaitingApprovalInfo waiting,
        CancellationToken cancellationToken)
    {
        var slaDeadline = DateTimeOffset.UtcNow.AddHours(24).ToString("o");
        var approval = new ApprovalRequest
        {
            Id = $"apr_{Guid.NewGuid():N}",
            RunId = runId,
            WorkflowName = workflowName,
            ActionRequested = waiting.NodeName ?? waiting.PurposeType,
            Requester = requester,
            AgentName = waiting.AgentName ?? string.Empty,
            PolicyRationale = waiting.PolicyTag,
            RiskScore = waiting.RiskScore,
            RiskLevel = waiting.RiskLevel,
            RiskFactors = waiting.RiskFactors?.ToList() ?? [],
            AffectedSystems = waiting.AffectedSystems?.ToList() ?? [],
            SlaDeadline = slaDeadline,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            Status = PendingApprovalStatus,
            Priority = DeriveApprovalPriority(waiting.RiskLevel),
        };

        await _approvalRepository.AddApprovalAsync(approval, cancellationToken);
        await _approvalRepository.SaveChangesAsync(cancellationToken);
    }

    private static string DeriveApprovalPriority(string riskLevel) => riskLevel switch
    {
        "critical" or "high" => "urgent",
        "medium" => "normal",
        _ => "low"
    };
}

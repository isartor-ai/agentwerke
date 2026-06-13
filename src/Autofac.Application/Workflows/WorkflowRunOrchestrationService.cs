using Autofac.Domain.Persistence;

namespace Autofac.Application.Workflows;

public sealed class WorkflowRunOrchestrationService : IWorkflowRunOrchestrationService
{
    private const string ActiveStatus = "active";
    private const string WaitingUserStatus = "waiting_user";
    private const string PendingApprovalStatus = "pending";

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

        if (result.WaitingApproval is not null)
        {
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

        if (!string.Equals(resolvedStatus, "approved", StringComparison.Ordinal))
        {
            return new ResumeRunResult(command.RunId, run.Status, WaitingApproval: null);
        }

        var result = await _runner.ResumeAsync(
            command.RunId,
            workflow.BpmnXml,
            decidedBy,
            cancellationToken);

        if (result.WaitingApproval is not null)
        {
            await CreateApprovalRequestAsync(
                result.RunId,
                workflow.Name,
                run.RequestedBy,
                result.WaitingApproval,
                cancellationToken);
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
        WaitingApprovalInfo waitingApproval,
        CancellationToken cancellationToken)
    {
        var slaDeadline = DateTimeOffset.UtcNow.AddHours(24).ToString("o");
        var approval = new ApprovalRequest
        {
            Id = $"apr_{Guid.NewGuid():N}",
            RunId = runId,
            WorkflowName = workflowName,
            ActionRequested = waitingApproval.NodeName ?? waitingApproval.PurposeType,
            Requester = requester,
            AgentName = string.Empty,
            PolicyRationale = waitingApproval.PolicyTag,
            RiskScore = 0,
            RiskLevel = "low",
            SlaDeadline = slaDeadline,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            Status = PendingApprovalStatus,
            Priority = "normal"
        };

        await _approvalRepository.AddApprovalAsync(approval, cancellationToken);
        await _approvalRepository.SaveChangesAsync(cancellationToken);
    }
}

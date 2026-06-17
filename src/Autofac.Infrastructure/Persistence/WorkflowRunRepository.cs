using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Infrastructure.Persistence;

public sealed class WorkflowRunRepository : IWorkflowRunRepository
{
    private readonly AutofacDbContext _dbContext;

    public WorkflowRunRepository(AutofacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        return _dbContext.WorkflowRuns
            .AsNoTracking()
            .Include(r => r.Steps)
            .Include(r => r.Events)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
    }

    public async Task<WorkflowRun> CreatePendingRunAsync(
        string runId,
        string workflowId,
        string workflowName,
        string workflowVersion,
        string? initiator,
        List<string> tags,
        string? correlationId,
        WorkflowRunCamundaLink? camunda,
        CancellationToken cancellationToken)
    {
        var run = new WorkflowRun
        {
            Id = runId,
            WorkflowId = workflowId,
            WorkflowName = workflowName,
            WorkflowVersion = workflowVersion,
            Status = "pending",
            RequestedBy = initiator ?? string.Empty,
            StartedAt = DateTimeOffset.UtcNow.ToString("o"),
            Tags = tags,
            CorrelationId = correlationId,
            CamundaProcessInstanceKey = camunda?.ProcessInstanceKey,
            CamundaProcessDefinitionKey = camunda?.ProcessDefinitionKey,
            CamundaProcessDefinitionId = camunda?.ProcessDefinitionId,
            CamundaProcessDefinitionVersion = camunda?.ProcessDefinitionVersion,
        };
        _dbContext.WorkflowRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task UpdateRunStatusAsync(string runId, string status, CancellationToken cancellationToken)
    {
        var run = await _dbContext.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
        run.Status = status;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCurrentStepAsync(string runId, string? currentStep, CancellationToken cancellationToken)
    {
        var run = await _dbContext.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
        run.CurrentStep = currentStep ?? string.Empty;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task IncrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken)
    {
        var run = await _dbContext.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
        run.PendingApprovals++;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DecrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken)
    {
        var run = await _dbContext.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
        run.PendingApprovals = Math.Max(0, run.PendingApprovals - 1);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AppendEventAsync(string runId, string type, string message, CancellationToken cancellationToken)
    {
        var run = await _dbContext.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow run '{runId}' not found.");
        run.Events.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Message = message,
            CreatedAt = DateTime.UtcNow.ToString("o")
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

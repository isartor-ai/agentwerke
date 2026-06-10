using Autofac.Domain.Persistence;
using Autofac.Workflows.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Infrastructure.Persistence;

public sealed class WorkflowRuntimeStore : IWorkflowRuntimeStore
{
    private readonly AutofacDbContext _dbContext;

    public WorkflowRuntimeStore(AutofacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WorkflowRun> CreateRunAsync(
        Guid workflowDefinitionId,
        string? initiator,
        CancellationToken cancellationToken)
    {
        var run = new WorkflowRun
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflowDefinitionId,
            Status = "created",
            Initiator = initiator,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.WorkflowRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<WorkflowRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        return await _dbContext.WorkflowRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(static run => run.Id == runId, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowEvent>> ListRunEventsAsync(Guid runId, CancellationToken cancellationToken)
    {
        return await _dbContext.WorkflowEvents
            .AsNoTracking()
            .Where(runEvent => runEvent.WorkflowRunId == runId)
            .OrderBy(runEvent => runEvent.CreatedAtUtc)
            .ThenBy(runEvent => runEvent.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AppendEventAsync(
        Guid runId,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var runEvent = new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowRunId = runId,
            EventType = eventType,
            PayloadJson = payloadJson,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.WorkflowEvents.Add(runEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRunStatusAsync(
        Guid runId,
        string status,
        DateTimeOffset? completedAtUtc,
        CancellationToken cancellationToken)
    {
        var run = await _dbContext.WorkflowRuns.FirstOrDefaultAsync(static r => r.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow run '{runId}' does not exist.");

        run.Status = status;
        run.CompletedAtUtc = completedAtUtc;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
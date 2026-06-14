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
        string workflowDefinitionId,
        string? initiator,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        var workflowDefinition = await _dbContext.WorkflowDefinitions.AsNoTracking().FirstAsync(x => x.Id == workflowDefinitionId, cancellationToken);
        var run = new WorkflowRun
        {
            Id = Guid.NewGuid().ToString(),
            WorkflowId = workflowDefinitionId,
            WorkflowName = workflowDefinition.Name,
            WorkflowVersion = workflowDefinition.Version,
            Status = "pending",
            RequestedBy = initiator ?? "unknown",
            StartedAt = DateTime.UtcNow.ToString("o"),
            Tags = workflowDefinition.Tags,
            CorrelationId = correlationId
        };

        _dbContext.WorkflowRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        return await _dbContext.WorkflowRuns
            .AsNoTracking()
            .Include(r => r.Steps)
            .Include(r => r.Events)
            .FirstOrDefaultAsync(run => run.Id == runId, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowEvent>> ListRunEventsAsync(string runId, CancellationToken cancellationToken)
    {
        var run = await GetRunAsync(runId, cancellationToken);
        return run?.Events.AsReadOnly() ?? new List<WorkflowEvent>().AsReadOnly();
    }

    public async Task AppendEventAsync(
        string runId,
        string type,
        string message,
        CancellationToken cancellationToken)
    {
        var run = await _dbContext.WorkflowRuns.FirstAsync(r => r.Id == runId, cancellationToken);
        var runEvent = new WorkflowEvent
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Message = message,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };

        run.Events.Add(runEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRunStatusAsync(
        string runId,
        string status,
        string? completedAt,
        CancellationToken cancellationToken)
    {
        var run = await _dbContext.WorkflowRuns.FirstAsync(r => r.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow run '{runId}' does not exist.");

        run.Status = status;
        run.CompletedAt = completedAt;
        if (completedAt != null)
        {
            var startedAt = DateTime.Parse(run.StartedAt);
            var completedAtDate = DateTime.Parse(completedAt);
            run.DurationMs = (int)(completedAtDate - startedAt).TotalMilliseconds;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkflowRunStep> CreateStepAsync(
        string runId,
        string nodeId,
        string? nodeName,
        string nodeType,
        string? agentName,
        CancellationToken cancellationToken)
    {
        var run = await _dbContext.WorkflowRuns.FirstAsync(r => r.Id == runId, cancellationToken);
        var step = new WorkflowRunStep
        {
            Id = $"step_{Guid.NewGuid():N}",
            Name = nodeName ?? nodeId,
            Type = nodeType,
            Status = "running",
            StartedAt = DateTime.UtcNow.ToString("o"),
            AgentName = agentName
        };

        run.Steps.Add(step);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return step;
    }

    public async Task UpdateStepStatusAsync(
        string stepId,
        string status,
        string? output,
        string? completedAt,
        PolicyDecision? policyDecision,
        CancellationToken cancellationToken)
    {
        var step = await _dbContext.WorkflowRunSteps.FirstAsync(s => s.Id == stepId, cancellationToken);
        step.Status = status;
        step.Output = output;
        step.CompletedAt = completedAt;
        step.PolicyDecision = policyDecision;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

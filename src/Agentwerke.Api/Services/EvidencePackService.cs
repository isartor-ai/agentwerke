using Agentwerke.Application.Workflows;
using Agentwerke.Infrastructure;
using Agentwerke.Infrastructure.Persistence;
using Agentwerke.Storage.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Agentwerke.Api.Services;

public sealed class EvidencePackService : IEvidencePackService
{
    private readonly AgentwerkeDbContext _dbContext;
    private readonly IArtifactStorage _artifactStorage;
    private readonly WorkflowRuntimeOptions _runtimeOptions;

    public EvidencePackService(
        AgentwerkeDbContext dbContext,
        IArtifactStorage artifactStorage,
        WorkflowRuntimeOptions runtimeOptions)
    {
        _dbContext = dbContext;
        _artifactStorage = artifactStorage;
        _runtimeOptions = runtimeOptions;
    }

    public async Task<EvidencePack> GenerateAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await _dbContext.WorkflowRuns
            .AsNoTracking()
            .Include(item => item.Steps)
            .Include(item => item.Events)
            .FirstOrDefaultAsync(item => item.Id == runId, cancellationToken)
            ?? throw new EvidencePackNotFoundException(runId);

        var workflow = await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == run.WorkflowId, cancellationToken);

        var approvals = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Where(item => item.RunId == runId)
            .ToListAsync(cancellationToken);

        var auditRecords = await _dbContext.AuditRecords
            .AsNoTracking()
            .Where(item => item.RunId == runId)
            .ToListAsync(cancellationToken);

        var runContext = await _dbContext.RunContextEntries
            .AsNoTracking()
            .Where(item => item.RunId == runId)
            .ToListAsync(cancellationToken);

        var artifacts = await _artifactStorage.ListAsync(runId, cancellationToken);
        var artifactInputs = artifacts
            .Select(item => new EvidenceArtifactInput(
                item.Name,
                item.SizeBytes,
                item.LastModifiedAt))
            .ToArray();

        return EvidencePackBuilder.Build(
            run,
            workflow,
            approvals,
            auditRecords,
            runContext,
            artifactInputs,
            runtimeMode: _runtimeOptions.Mode.ToString(),
            camundaEnabled: _runtimeOptions.IsCamundaMode,
            generatedAt: DateTimeOffset.UtcNow);
    }
}

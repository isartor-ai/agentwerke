using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Infrastructure.Persistence;

public sealed class OutboxRepository : IRunOutbox
{
    private readonly AutofacDbContext _db;

    public OutboxRepository(AutofacDbContext db) => _db = db;

    public async Task EnqueueAsync(
        string operation,
        string runId,
        string? payload = null,
        DateTimeOffset? visibleAfter = null,
        CancellationToken ct = default)
    {
        _db.OutboxEntries.Add(new OutboxEntry
        {
            Id = $"out_{Guid.NewGuid():N}",
            Operation = operation,
            RunId = runId,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            VisibleAfter = visibleAfter ?? DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<OutboxEntry?> TryClaimNextAsync(string workerId, CancellationToken ct = default)
    {
        // Find the oldest unclaimed, visible, incomplete entry
        var candidate = await _db.OutboxEntries
            .Where(e => e.LockedBy == null && e.CompletedAt == null && e.FailedAt == null
                        && e.VisibleAfter <= DateTimeOffset.UtcNow)
            .OrderBy(e => e.VisibleAfter)
            .FirstOrDefaultAsync(ct);

        if (candidate is null)
            return null;

        // Atomic conditional claim — only succeeds if still unclaimed
        var now = DateTimeOffset.UtcNow;
        var affected = await _db.OutboxEntries
            .Where(e => e.Id == candidate.Id && e.LockedBy == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.LockedBy, workerId)
                       .SetProperty(e => e.LockedAt, now)
                       .SetProperty(e => e.AttemptCount, candidate.AttemptCount + 1),
                ct);

        if (affected == 0)
            return null;

        candidate.LockedBy = workerId;
        candidate.LockedAt = now;
        candidate.AttemptCount++;
        return candidate;
    }

    public async Task MarkCompletedAsync(string entryId, CancellationToken ct = default)
    {
        await _db.OutboxEntries
            .Where(e => e.Id == entryId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.CompletedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public async Task MarkFailedAsync(string entryId, string error, CancellationToken ct = default)
    {
        await _db.OutboxEntries
            .Where(e => e.Id == entryId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.FailedAt, DateTimeOffset.UtcNow)
                       .SetProperty(e => e.Error, error),
                ct);
    }

    public async Task<IReadOnlyList<string>> ListStuckRunIdsAsync(CancellationToken ct = default)
    {
        // "Stuck" = running status with no claimed outbox entry and no completed/failed entry in last 10 min
        var staleThreshold = DateTimeOffset.UtcNow.AddMinutes(-10);

        var stuckIds = await _db.WorkflowRuns
            .Where(r => r.Status == "running"
                        && !_db.OutboxEntries.Any(e =>
                            e.RunId == r.Id &&
                            e.CompletedAt == null &&
                            e.FailedAt == null))
            .Where(r => r.StartedAt != null && string.Compare(r.StartedAt, staleThreshold.ToString("o"), StringComparison.Ordinal) < 0)
            .Select(r => r.Id)
            .ToListAsync(ct);

        return stuckIds;
    }
}

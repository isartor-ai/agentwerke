namespace Autofac.Domain.Persistence;

public sealed class OutboxEntry
{
    public string Id { get; set; } = string.Empty;
    public required string Operation { get; set; }
    public required string RunId { get; set; }
    public string? Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset VisibleAfter { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }
}

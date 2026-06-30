namespace Autofac.Application.Notifications;

/// <summary>
/// Sends an out-of-band notification when a run reaches a human-approval gate.
/// Implementations are best-effort: a delivery failure must never fail the run (#31).
/// </summary>
public interface IApprovalNotifier
{
    Task NotifyApprovalRequestedAsync(
        ApprovalNotification notification,
        CancellationToken cancellationToken = default);
}

/// <summary>Details surfaced to a notification channel when an approval is requested.</summary>
public sealed record ApprovalNotification(
    string RunId,
    string ApprovalId,
    string WorkflowName,
    string ActionRequested,
    string? RiskLevel = null,
    string? ArtifactName = null);

/// <summary>No-op default used when no notification channels are wired in.</summary>
public sealed class NullApprovalNotifier : IApprovalNotifier
{
    public Task NotifyApprovalRequestedAsync(
        ApprovalNotification notification,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

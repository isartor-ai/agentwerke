using Autofac.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofac.Integrations;

/// <summary>
/// Fans an approval-requested event out to the enabled chat connectors (Slack, Teams).
/// Best-effort: any delivery failure is logged and swallowed so a notification problem
/// never blocks the run (#31).
/// </summary>
public sealed class ConnectorApprovalNotifier : IApprovalNotifier
{
    private readonly ISlackConnector _slack;
    private readonly ITeamsConnector _teams;
    private readonly IntegrationOptions _options;
    private readonly ILogger<ConnectorApprovalNotifier> _logger;

    public ConnectorApprovalNotifier(
        ISlackConnector slack,
        ITeamsConnector teams,
        IOptions<IntegrationOptions> options,
        ILogger<ConnectorApprovalNotifier> logger)
    {
        _slack = slack;
        _teams = teams;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyApprovalRequestedAsync(
        ApprovalNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Notifications.OnApprovalRequested)
            return;

        var command = BuildCommand(notification);

        if (_options.Slack.Enabled)
            await SendAsync("slack", () => _slack.SendNotificationAsync(command, cancellationToken));

        if (_options.Teams.Enabled)
            await SendAsync("teams", () => _teams.SendNotificationAsync(command, cancellationToken));
    }

    private static SendNotificationCommand BuildCommand(ApprovalNotification n)
    {
        var lines = new List<string>
        {
            $"Run `{n.RunId}` is waiting for approval.",
            $"Action: {n.ActionRequested}",
        };
        if (!string.IsNullOrWhiteSpace(n.RiskLevel))
            lines.Add($"Risk: {n.RiskLevel}");
        if (!string.IsNullOrWhiteSpace(n.ArtifactName))
            lines.Add($"Artifact: {n.ArtifactName}");

        return new SendNotificationCommand(
            $"Approval needed: {n.WorkflowName}",
            string.Join("\n", lines));
    }

    private async Task SendAsync(string channel, Func<Task<NotificationResult>> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Approval notification to {Channel} failed; continuing.", channel);
        }
    }
}

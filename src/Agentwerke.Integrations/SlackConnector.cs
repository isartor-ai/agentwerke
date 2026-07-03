using System.Text;
using System.Text.Json;
using Agentwerke.AgentSecOps;
using Agentwerke.Application.Observability;
using Agentwerke.Application.Secrets;
using Microsoft.Extensions.Options;

namespace Agentwerke.Integrations;

public sealed record SendNotificationCommand(
    string Title,
    string Message,
    string? ApprovalId = null,
    string? RunId = null);

public sealed record NotificationResult(string Summary);

public interface ISlackConnector
{
    Task<NotificationResult> SendNotificationAsync(
        SendNotificationCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class SlackConnector : ConnectorBase, ISlackConnector
{
    private readonly HttpClient _httpClient;
    private readonly SlackOptions _options;
    private readonly ISecretStore _secretStore;

    public SlackConnector(
        HttpClient httpClient,
        IOptions<IntegrationOptions> options,
        ISecretStore secretStore,
        IPolicyEvaluationService policyEvaluationService,
        IAuditRepository auditRepository,
        IWorkflowMetrics metrics,
        ICorrelationContext correlationContext,
        IWorkflowTracer tracer)
        : base(policyEvaluationService, auditRepository, metrics, correlationContext, tracer)
    {
        _httpClient = httpClient;
        _options = options.Value.Slack;
        _secretStore = secretStore;
    }

    public override string ConnectorId => "slack";

    public override string DisplayName => "Slack";

    public override bool Enabled => _options.Enabled;

    public override IReadOnlyList<string> SupportedOperations => ["send_notification"];

    public async Task<NotificationResult> SendNotificationAsync(
        SendNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        var webhookUrl = await ResolveWebhookUrlAsync(cancellationToken);
        var payload = command.ApprovalId is { Length: > 0 } approvalId
            ? BuildInteractivePayload(command, approvalId)
            : new { text = $"*{command.Title}*\n{command.Message}" };

        using var response = await _httpClient.PostAsync(
            webhookUrl,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return new NotificationResult($"Slack notification sent: {command.Title}");
    }

    // Interactive approval message (#172): Approve/Reject buttons whose value carries
    // "{approvalId}:{runId}" so the interactions endpoint can decide without a lookup.
    private static object BuildInteractivePayload(SendNotificationCommand command, string approvalId)
    {
        var value = $"{approvalId}:{command.RunId}";
        var body = $"*{command.Title}*\n{command.Message}";
        return new
        {
            text = body,
            blocks = new object[]
            {
                new { type = "section", text = new { type = "mrkdwn", text = body } },
                new
                {
                    type = "actions",
                    elements = new object[]
                    {
                        new { type = "button", action_id = "approve", style = "primary", text = new { type = "plain_text", text = "Approve" }, value },
                        new { type = "button", action_id = "reject", style = "danger", text = new { type = "plain_text", text = "Reject" }, value },
                    },
                },
            },
        };
    }

    protected override async Task<ConnectorExecutionResult> ExecuteAllowedAsync(ConnectorExecutionRequest request, CancellationToken cancellationToken)
    {
        var command = request.Payload.Deserialize<SendNotificationCommand>()
            ?? throw new InvalidOperationException("Slack payload was empty.");
        var result = await SendNotificationAsync(command, cancellationToken);
        return new ConnectorExecutionResult(true, "completed", result.Summary);
    }

    private async Task<string> ResolveWebhookUrlAsync(CancellationToken cancellationToken)
    {
        return await _secretStore.GetSecretAsync("Integrations:Slack:WebhookUrl", cancellationToken)
            ?? _options.WebhookUrl;
    }
}

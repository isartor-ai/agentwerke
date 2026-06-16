using System.Text;
using System.Text.Json;
using Autofac.AgentSecOps;
using Autofac.Application.Observability;
using Autofac.Application.Secrets;
using Microsoft.Extensions.Options;

namespace Autofac.Integrations;

public sealed record SendNotificationCommand(string Title, string Message);

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
        using var response = await _httpClient.PostAsync(
            webhookUrl,
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    text = $"*{command.Title}*\n{command.Message}"
                }),
                Encoding.UTF8,
                "application/json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return new NotificationResult($"Slack notification sent: {command.Title}");
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

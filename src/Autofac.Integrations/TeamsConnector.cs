using System.Text;
using System.Text.Json;
using Autofac.AgentSecOps;
using Autofac.Application.Observability;
using Autofac.Application.Secrets;
using Microsoft.Extensions.Options;

namespace Autofac.Integrations;

public interface ITeamsConnector
{
    Task<NotificationResult> SendNotificationAsync(
        SendNotificationCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class TeamsConnector : ConnectorBase, ITeamsConnector
{
    private readonly HttpClient _httpClient;
    private readonly TeamsOptions _options;
    private readonly ISecretStore _secretStore;

    public TeamsConnector(
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
        _options = options.Value.Teams;
        _secretStore = secretStore;
    }

    public override string ConnectorId => "teams";

    public override string DisplayName => "Microsoft Teams";

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
                    text = $"**{command.Title}**\n\n{command.Message}"
                }),
                Encoding.UTF8,
                "application/json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return new NotificationResult($"Teams notification sent: {command.Title}");
    }

    protected override async Task<ConnectorExecutionResult> ExecuteAllowedAsync(ConnectorExecutionRequest request, CancellationToken cancellationToken)
    {
        var command = request.Payload.Deserialize<SendNotificationCommand>()
            ?? throw new InvalidOperationException("Teams payload was empty.");
        var result = await SendNotificationAsync(command, cancellationToken);
        return new ConnectorExecutionResult(true, "completed", result.Summary);
    }

    private async Task<string> ResolveWebhookUrlAsync(CancellationToken cancellationToken)
    {
        return await _secretStore.GetSecretAsync("Integrations:Teams:WebhookUrl", cancellationToken)
            ?? _options.WebhookUrl;
    }
}

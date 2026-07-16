using System.Text;
using System.Text.Json;
using Agentwerke.Application.Agents;
using Agentwerke.Application.Secrets;
using Agentwerke.Domain.Persistence;
using Agentwerke.Integrations.Webhooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentwerke.Integrations.Channels;

/// <summary>
/// Posts an interaction to a configured endpoint and accepts a signed response back (#224).
///
/// The reference implementation of <see cref="IInteractionChannel"/>: no provider quirks, so it proves
/// the whole outbound → inbound → resume loop. Slack (#225) then only has to translate payload shapes.
/// </summary>
public sealed class WebhookInteractionChannel : IInteractionChannel
{
    private readonly HttpClient _httpClient;
    private readonly InteractionWebhookOptions _options;
    private readonly ISecretStore _secretStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebhookInteractionChannel> _logger;

    public WebhookInteractionChannel(
        HttpClient httpClient,
        IOptions<IntegrationOptions> options,
        ISecretStore secretStore,
        TimeProvider timeProvider,
        ILogger<WebhookInteractionChannel> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.InteractionWebhook;
        _secretStore = secretStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string ChannelId => InteractionChannels.Webhook;

    public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Endpoint);

    public bool CanCarryResponse => true;

    public async Task<InteractionDeliveryResult> DeliverAsync(
        InteractionDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var secret = await ResolveSecretAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Fail rather than post unsigned: an unsigned outbound means the receiver cannot tell a
            // genuine question from a forged one.
            _logger.LogError(
                "Interaction webhook is enabled but no secret resolved; refusing to send unsigned. "
                + "InteractionId={InteractionId}",
                request.Interaction.Id);
            return InteractionDeliveryResult.Failed("Interaction webhook secret is not configured.");
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(BuildPayload(request));
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString();

        using var message = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        message.Headers.TryAddWithoutValidation(
            "X-Agentwerke-Signature", WebhookSignatureValidator.SignAgentwerke(body, timestamp, secret));
        message.Headers.TryAddWithoutValidation("X-Agentwerke-Timestamp", timestamp);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 10));

        using var response = await _httpClient.SendAsync(message, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return InteractionDeliveryResult.Failed(
                $"Endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        return InteractionDeliveryResult.Delivered(await ReadMessageIdAsync(response, timeout.Token));
    }

    /// <summary>
    /// Deliberately narrow. This payload leaves the trust boundary, so it carries the prompt, the
    /// choices, the identifiers needed to correlate, and a link back — never run context, artifacts,
    /// tool output or credentials.
    /// </summary>
    private object BuildPayload(InteractionDeliveryRequest request)
    {
        var i = request.Interaction;
        return new
        {
            interactionId = i.Id,
            runId = request.RunId,
            stepId = i.StepId,
            workflowName = request.WorkflowName,
            fromAgent = i.FromAgent,
            kind = i.Kind,
            blocking = i.Blocking,
            prompt = i.Prompt,
            options = i.Options,
            createdAt = i.CreatedAt,
            timeoutAt = i.TimeoutAt,
            respondUrl = request.RespondUrl,
        };
    }

    private static async Task<string?> ReadMessageIdAsync(HttpResponseMessage response, CancellationToken ct)
    {
        // A message id is a convenience for correlating a later callback; a receiver that returns
        // nothing useful is not an error.
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<string?> ResolveSecretAsync(CancellationToken cancellationToken)
    {
        var configured = _options.Secret;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        return await _secretStore.GetSecretAsync(configured, cancellationToken) ?? configured;
    }
}

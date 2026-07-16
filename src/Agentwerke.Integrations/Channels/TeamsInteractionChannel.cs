using System.Text;
using System.Text.Json;
using Agentwerke.Application.Agents;
using Agentwerke.Application.Secrets;
using Agentwerke.Domain.Persistence;
using Microsoft.Extensions.Options;

namespace Agentwerke.Integrations.Channels;

/// <summary>
/// Sends interactions through a Teams incoming webhook. Incoming webhooks are one-way, so this
/// channel cannot carry a response; blocking cards deep-link to Agentwerke instead.
/// </summary>
public sealed class TeamsInteractionChannel : IInteractionChannel
{
    private readonly HttpClient _httpClient;
    private readonly TeamsOptions _options;
    private readonly ISecretStore _secretStore;

    public TeamsInteractionChannel(
        HttpClient httpClient,
        IOptions<IntegrationOptions> options,
        ISecretStore secretStore)
    {
        _httpClient = httpClient;
        _options = options.Value.Teams;
        _secretStore = secretStore;
    }

    public string ChannelId => InteractionChannels.Teams;
    public bool Enabled => _options.Enabled;

    // A Teams incoming webhook is outbound-only. Supporting replies requires a Bot/Graph app with
    // token validation; flipping this flag cannot make the webhook bidirectional.
    public bool CanCarryResponse => false;

    public async Task<InteractionDeliveryResult> DeliverAsync(
        InteractionDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var webhookUrl = await ResolveWebhookUrlAsync(cancellationToken);
            var body = JsonSerializer.Serialize(BuildCard(request));
            using var response = await _httpClient.PostAsync(
                webhookUrl,
                new StringContent(body, Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return InteractionDeliveryResult.Failed(
                    $"Teams webhook returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            return request.Interaction.Blocking
                ? InteractionDeliveryResult.NotSupported(
                    "Teams incoming webhooks cannot accept replies; answer in Agentwerke or another response-capable channel.")
                : InteractionDeliveryResult.Delivered();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return InteractionDeliveryResult.Failed($"Teams webhook delivery failed: {ex.Message}");
        }
    }

    private static object BuildCard(InteractionDeliveryRequest request)
    {
        var interaction = request.Interaction;
        var facts = new List<object>
        {
            new { title = "Agent", value = interaction.FromAgent },
            new { title = "Workflow", value = request.WorkflowName ?? "Unknown" },
            new { title = "Run", value = request.RunId },
            new { title = "Step", value = interaction.StepId ?? "—" },
        };

        var body = new List<object>
        {
            new { type = "TextBlock", size = "Medium", weight = "Bolder", text = interaction.Blocking ? "Agent interaction needs attention" : "Agent notification", wrap = true },
            new { type = "FactSet", facts },
            new { type = "TextBlock", text = interaction.Prompt, wrap = true },
        };

        if (interaction.Options.Count > 0)
        {
            body.Add(new
            {
                type = "TextBlock",
                text = $"Available choices: {string.Join(", ", interaction.Options)}",
                wrap = true,
                isSubtle = true,
            });
        }

        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    contentUrl = (string?)null,
                    content = new Dictionary<string, object?>
                    {
                        ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                        ["type"] = "AdaptiveCard",
                        ["version"] = "1.4",
                        ["body"] = body,
                        ["actions"] = string.IsNullOrWhiteSpace(request.RespondUrl)
                            ? Array.Empty<object>()
                            : new object[]
                            {
                                new
                                {
                                    type = "Action.OpenUrl",
                                    title = interaction.Blocking ? "Answer in Agentwerke" : "Open run in Agentwerke",
                                    url = request.RespondUrl,
                                }
                            },
                    },
                }
            },
        };
    }

    private async Task<string> ResolveWebhookUrlAsync(CancellationToken cancellationToken) =>
        await _secretStore.GetSecretAsync("Integrations:Teams:WebhookUrl", cancellationToken)
        ?? _options.WebhookUrl;
}

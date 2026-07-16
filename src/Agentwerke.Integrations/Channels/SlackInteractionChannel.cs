using System.Text;
using System.Text.Json;
using Agentwerke.Application.Agents;
using Agentwerke.Application.Secrets;
using Agentwerke.Domain.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentwerke.Integrations.Channels;

/// <summary>
/// Delivers an interaction to Slack as a Block Kit message (#225).
///
/// Slack already has a working approval path — an incoming-webhook post with approve/reject buttons,
/// and an inbound callback at webhooks/slack/interactions. This channel shares that callback URL, so
/// its action ids are namespaced (<c>interaction_*</c>) and cannot collide with the approval path's
/// bare <c>approve</c>/<c>reject</c>. Breaking approvals is the main risk in this ticket.
/// </summary>
public sealed class SlackInteractionChannel : IInteractionChannel
{
    /// <summary>Action id prefix. The approval path keys on bare "approve"/"reject" — never reuse those.</summary>
    public const string ActionPrefix = "interaction_";

    public const string ChoiceActionId = ActionPrefix + "choice";
    public const string AnswerActionId = ActionPrefix + "answer";

    private readonly HttpClient _httpClient;
    private readonly SlackOptions _options;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<SlackInteractionChannel> _logger;

    public SlackInteractionChannel(
        HttpClient httpClient,
        IOptions<IntegrationOptions> options,
        ISecretStore secretStore,
        ILogger<SlackInteractionChannel> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.Slack;
        _secretStore = secretStore;
        _logger = logger;
    }

    public string ChannelId => InteractionChannels.Slack;

    public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.WebhookUrl);

    public bool CanCarryResponse => true;

    public async Task<InteractionDeliveryResult> DeliverAsync(
        InteractionDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var webhookUrl = await ResolveWebhookUrlAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return InteractionDeliveryResult.Failed("Slack webhook URL is not configured.");
        }

        var payload = BuildPayload(request);

        using var response = await _httpClient.PostAsync(
            webhookUrl,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return InteractionDeliveryResult.Failed(
                $"Slack returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        // An incoming webhook replies "ok", not a message ts. Without a ts we cannot later update the
        // message to show it is spent, so free-text and message updates need a bot token — see
        // BuildPayload's degradation note.
        return InteractionDeliveryResult.Delivered(channelMessageId: null);
    }

    /// <summary>
    /// Renders the interaction. Carries prompt, choices, identifiers and a link only — this payload
    /// leaves the trust boundary, so never run context, artifacts, tool output or credentials.
    /// </summary>
    private object BuildPayload(InteractionDeliveryRequest request)
    {
        var interaction = request.Interaction;
        var heading = interaction.Kind switch
        {
            AgentInteractionKinds.Confirm => $":lock: *{interaction.FromAgent}* needs a confirmation",
            AgentInteractionKinds.Notify => $":information_source: *{interaction.FromAgent}* says",
            _ => $":question: *{interaction.FromAgent}* has a question",
        };

        var context = string.Join(
            "  |  ",
            new[]
            {
                request.WorkflowName is { Length: > 0 } w ? $"Workflow: {w}" : null,
                $"Run: {request.RunId}",
                interaction.StepId is { Length: > 0 } s ? $"Step: {s}" : null,
            }.Where(part => part is not null));

        var blocks = new List<object>
        {
            new { type = "section", text = new { type = "mrkdwn", text = $"{heading}\n\n{interaction.Prompt}" } },
            new { type = "context", elements = new object[] { new { type = "mrkdwn", text = context } } },
        };

        var elements = BuildActionElements(interaction, request.RespondUrl);
        if (elements.Count > 0)
        {
            blocks.Add(new { type = "actions", elements = elements.ToArray() });
        }

        return new { text = $"{interaction.FromAgent}: {interaction.Prompt}", blocks = blocks.ToArray() };
    }

    private List<object> BuildActionElements(AgentInteraction interaction, string respondUrl)
    {
        // Nothing to answer: a notification needs no controls.
        if (!interaction.Blocking)
        {
            return [];
        }

        if (interaction.Options.Count > 0)
        {
            return interaction.Options
                .Select(option => (object)new
                {
                    type = "button",
                    action_id = $"{ChoiceActionId}:{option}",
                    style = StyleFor(option),
                    text = new { type = "plain_text", text = Capitalise(option) },
                    value = $"{interaction.Id}:{option}",
                })
                .ToList();
        }

        // Free text needs a modal (views.open), which needs a bot token — an incoming webhook alone
        // cannot open one. Rather than render a button that errors when clicked, degrade to a link
        // back to the UI and say so.
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            _logger.LogDebug(
                "Slack free-text answering needs a bot token; falling back to an Agentwerke link. "
                + "InteractionId={InteractionId}",
                interaction.Id);

            return string.IsNullOrWhiteSpace(respondUrl)
                ? []
                : [new { type = "button", action_id = $"{ActionPrefix}open", text = new { type = "plain_text", text = "Answer in Agentwerke" }, url = respondUrl }];
        }

        return
        [
            new
            {
                type = "button",
                action_id = AnswerActionId,
                style = "primary",
                text = new { type = "plain_text", text = "Answer" },
                value = interaction.Id,
            },
        ];
    }

    private static string? StyleFor(string option) => option.ToLowerInvariant() switch
    {
        "approve" or "yes" => "primary",
        "reject" or "no" => "danger",
        _ => null,
    };

    private static string Capitalise(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private async Task<string?> ResolveWebhookUrlAsync(CancellationToken cancellationToken)
    {
        var configured = _options.WebhookUrl;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        return await _secretStore.GetSecretAsync(configured, cancellationToken) ?? configured;
    }
}

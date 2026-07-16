using System.Net;
using System.Text.Json;
using Agentwerke.Application.Agents;
using Agentwerke.Application.Secrets;
using Agentwerke.Domain.Persistence;
using Agentwerke.Integrations.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentwerke.Integrations.Tests;

/// <summary>
/// Slack Block Kit rendering (#225).
///
/// The load-bearing assertion is the action-id namespace: interactions share Slack's single callback
/// URL with the existing approval path, which keys on bare "approve"/"reject". A collision there
/// would break a working feature, which is this ticket's main risk.
/// </summary>
public sealed class SlackInteractionChannelTests
{
    [Fact]
    public async Task Deliver_ChoiceInteraction_RendersOneNamespacedButtonPerOption()
    {
        var handler = new CapturingHandler();
        var channel = Channel(handler);

        var result = await channel.DeliverAsync(Request(Interaction(i => i.Options = ["approve", "reject"])), CancellationToken.None);

        Assert.Equal(InteractionDeliveryStatuses.Delivered, result.Status);
        var elements = Elements(handler.LastBody!);
        Assert.Equal(2, elements.GetArrayLength());

        foreach (var element in elements.EnumerateArray())
        {
            var actionId = element.GetProperty("action_id").GetString()!;
            Assert.StartsWith(SlackInteractionChannel.ActionPrefix, actionId);

            // The approval path parses these exact ids; colliding would hijack it.
            Assert.NotEqual("approve", actionId);
            Assert.NotEqual("reject", actionId);
        }
    }

    [Fact]
    public async Task Deliver_ChoiceButtonsCarryTheInteractionIdAndOption()
    {
        var handler = new CapturingHandler();
        var channel = Channel(handler);

        await channel.DeliverAsync(Request(Interaction(i => i.Options = ["approve", "reject"])), CancellationToken.None);

        var values = Elements(handler.LastBody!).EnumerateArray()
            .Select(e => e.GetProperty("value").GetString())
            .ToList();

        Assert.Equal(["int_1:approve", "int_1:reject"], values);
    }

    /// <summary>Free text needs views.open, which an incoming webhook cannot do — degrade, do not error.</summary>
    [Fact]
    public async Task Deliver_FreeTextWithoutABotToken_FallsBackToAnAgentwerkeLink()
    {
        var handler = new CapturingHandler();
        var channel = Channel(handler, botToken: null);

        await channel.DeliverAsync(Request(Interaction()), CancellationToken.None);

        var element = Elements(handler.LastBody!).EnumerateArray().Single();
        Assert.Equal("https://agentwerke.example/runs/run_1", element.GetProperty("url").GetString());
        Assert.False(element.TryGetProperty("value", out _));
    }

    [Fact]
    public async Task Deliver_FreeTextWithABotToken_OffersTheModalButton()
    {
        var handler = new CapturingHandler();
        var channel = Channel(handler, botToken: "xoxb-test");

        await channel.DeliverAsync(Request(Interaction()), CancellationToken.None);

        var element = Elements(handler.LastBody!).EnumerateArray().Single();
        Assert.Equal(SlackInteractionChannel.AnswerActionId, element.GetProperty("action_id").GetString());
        Assert.Equal("int_1", element.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Deliver_Notification_RendersNoControls()
    {
        var handler = new CapturingHandler();
        var channel = Channel(handler);

        await channel.DeliverAsync(
            Request(Interaction(i =>
            {
                i.Blocking = false;
                i.Kind = AgentInteractionKinds.Notify;
            })),
            CancellationToken.None);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var blocks = document.RootElement.GetProperty("blocks");
        Assert.DoesNotContain(
            blocks.EnumerateArray(),
            b => b.GetProperty("type").GetString() == "actions");
    }

    /// <summary>The payload crosses the trust boundary; it must carry nothing beyond the question.</summary>
    [Fact]
    public async Task Deliver_PayloadCarriesOnlyTheQuestionAndItsIdentifiers()
    {
        var handler = new CapturingHandler();
        var channel = Channel(handler);

        await channel.DeliverAsync(
            Request(Interaction(i => i.Intent = "super-secret-token-value")),
            CancellationToken.None);

        Assert.Contains("Deploy to production?", handler.LastBody);
        Assert.DoesNotContain("super-secret-token-value", handler.LastBody);
    }

    [Fact]
    public async Task Deliver_SlackOutage_ReturnsFailedRatherThanThrowing()
    {
        var handler = new CapturingHandler(HttpStatusCode.ServiceUnavailable);
        var channel = Channel(handler);

        var result = await channel.DeliverAsync(Request(Interaction()), CancellationToken.None);

        Assert.Equal(InteractionDeliveryStatuses.Failed, result.Status);
        Assert.Contains("503", result.Error);
    }

    [Fact]
    public void Enabled_RequiresBothTheFlagAndAWebhookUrl()
    {
        Assert.False(Channel(new CapturingHandler(), enabled: true, webhookUrl: "").Enabled);
        Assert.False(Channel(new CapturingHandler(), enabled: false).Enabled);
        Assert.True(Channel(new CapturingHandler()).Enabled);
    }

    [Fact]
    public void CanCarryResponse_IsTrue()
    {
        Assert.True(Channel(new CapturingHandler()).CanCarryResponse);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static JsonElement Elements(string body)
    {
        using var document = JsonDocument.Parse(body);
        var actions = document.RootElement.GetProperty("blocks").EnumerateArray()
            .Single(b => b.GetProperty("type").GetString() == "actions");
        return actions.GetProperty("elements").Clone();
    }

    private static InteractionDeliveryRequest Request(AgentInteraction interaction) =>
        new(interaction, "run_1", "Deploy pipeline", "https://agentwerke.example/runs/run_1");

    private static AgentInteraction Interaction(Action<AgentInteraction>? configure = null)
    {
        var interaction = new AgentInteraction
        {
            Id = "int_1",
            RunId = "run_1",
            StepId = "review",
            FromAgent = "reviewer",
            Kind = AgentInteractionKinds.Question,
            AddresseeType = AgentInteractionAddresseeTypes.Human,
            Blocking = true,
            Prompt = "Deploy to production?",
            Status = AgentInteractionStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };
        configure?.Invoke(interaction);
        return interaction;
    }

    private static SlackInteractionChannel Channel(
        CapturingHandler handler,
        bool enabled = true,
        string webhookUrl = "https://hooks.slack.example/T/B/X",
        string? botToken = null) =>
        new(
            new HttpClient(handler),
            Options.Create(new IntegrationOptions
            {
                Slack = new SlackOptions
                {
                    Enabled = enabled,
                    WebhookUrl = webhookUrl,
                    BotToken = botToken ?? string.Empty,
                },
            }),
            new PassthroughSecretStore(),
            NullLogger<SlackInteractionChannel>.Instance);

    private sealed class PassthroughSecretStore : ISecretStore
    {
        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(key);
    }

    private sealed class CapturingHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent("ok") };
        }
    }
}

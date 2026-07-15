using System.Net;
using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;
using Agentwerke.Integrations.Channels;
using Microsoft.Extensions.Options;

namespace Agentwerke.Integrations.Tests;

public sealed class TeamsInteractionChannelTests
{
    [Fact]
    public async Task BlockingInteraction_PostsCardWithRunLinkAndReportsNotSupported()
    {
        string? body = null;
        var channel = Create(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await channel.DeliverAsync(Request(blocking: true), CancellationToken.None);

        Assert.Equal(InteractionDeliveryStatuses.NotSupported, result.Status);
        Assert.Contains("Deploy", body);
        Assert.Contains("reviewer", body);
        Assert.Contains("run_1", body);
        Assert.Contains("step_1", body);
        Assert.Contains("Ship now?", body);
        Assert.Contains("Answer in Agentwerke", body);
        Assert.Contains("https://agentwerke.test/runs/run_1", body);
        Assert.Contains("Action.OpenUrl", body);
        Assert.DoesNotContain("Action.Execute", body);
    }

    [Fact]
    public async Task Notify_PostsCardAndReportsDelivered()
    {
        var channel = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await channel.DeliverAsync(Request(blocking: false), CancellationToken.None);

        Assert.Equal(InteractionDeliveryStatuses.Delivered, result.Status);
    }

    [Fact]
    public async Task TeamsOutage_ReturnsProviderFailureWithoutThrowing()
    {
        var channel = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            ReasonPhrase = "Teams unavailable"
        }));

        var result = await channel.DeliverAsync(Request(blocking: true), CancellationToken.None);

        Assert.Equal(InteractionDeliveryStatuses.Failed, result.Status);
        Assert.Contains("503", result.Error);
    }

    private static TeamsInteractionChannel Create(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) =>
        new(
            new HttpClient(new StubHandler(handler)),
            Options.Create(new IntegrationOptions
            {
                Teams = new TeamsOptions { Enabled = true, WebhookUrl = "https://teams.test/webhook" }
            }),
            new StubSecretStore());

    private static InteractionDeliveryRequest Request(bool blocking)
    {
        var interaction = new AgentInteraction
        {
            Id = "int_1",
            RunId = "run_1",
            StepId = "step_1",
            FromAgent = "reviewer",
            Kind = blocking ? AgentInteractionKinds.Choice : AgentInteractionKinds.Notify,
            AddresseeType = AgentInteractionAddresseeTypes.Human,
            Blocking = blocking,
            Prompt = "Ship now?",
            Options = ["ship", "wait"],
            Status = blocking ? AgentInteractionStatuses.Pending : AgentInteractionStatuses.Posted,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o")
        };
        return new InteractionDeliveryRequest(
            interaction, "run_1", "Deploy", "https://agentwerke.test/runs/run_1");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}

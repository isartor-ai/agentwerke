using System.Text.Json.Nodes;

namespace Agentwerke.E2ETests;

/// <summary>
/// End-to-end proof of the generic inbound interaction webhook (#224) against the deployed API,
/// Postgres and WireMock stack — the security-critical half of the channel work.
///
/// These need no model stubbing: they drive the signed HTTP endpoint directly, so they exercise the
/// real HMAC verifier, the replay window, and the fail-closed behaviour through the actual request
/// pipeline rather than a unit-test fake. The compose stack configures
/// <c>Integrations:InteractionWebhook:Secret</c> to the value used here.
///
/// Every assertion in this file was first confirmed by hand with curl against the running stack
/// (404/401/401/401) before being codified, so a failure here is a real regression, not a flaky
/// expectation.
/// </summary>
public sealed class InteractionWebhookE2ETests : E2ETestBase
{
    private const string Secret = "e2e-interaction-webhook-secret";

    [Fact]
    public async Task InboundResponse_NoSignature_FailsClosed()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

        // The endpoint resumes a parked run, so an unsigned request must be refused, not skipped —
        // unlike the Jira/GitHub trigger endpoints, which tolerate an unset secret.
        using var http = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
        var resp = await http.PostAsync(
            "/webhooks/interactions/response",
            new StringContent("{\"interactionId\":\"x\",\"response\":\"approve\"}",
                System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(401, (int)resp.StatusCode);
    }

    [Fact]
    public async Task InboundResponse_BadSignature_IsRejected()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

        var status = await Api.PostSignedWebhookResponseAsync(
            new { interactionId = "x", response = "approve" },
            Secret,
            signatureOverride: "sha256=deadbeef");

        Assert.Equal(401, status);
    }

    [Fact]
    public async Task InboundResponse_StaleTimestamp_IsRejectedAsReplay()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

        // Correctly signed for its (old) timestamp, but outside the freshness window: the exact shape
        // of a captured-and-replayed payload.
        var stale = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var status = await Api.PostSignedWebhookResponseAsync(
            new { interactionId = "x", response = "approve" },
            Secret,
            unixTimestampOverride: stale);

        Assert.Equal(401, status);
    }

    [Fact]
    public async Task InboundResponse_ValidSignatureUnknownInteraction_IsNotFound()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

        // Signature and freshness both pass, so the request reaches the handler; the interaction id is
        // unknown, so it is a clean 404 rather than a 401. This is the proof the verifier accepts a
        // genuinely valid request, complementing the three rejection cases.
        var status = await Api.PostSignedWebhookResponseAsync(
            new { interactionId = $"missing-{Guid.NewGuid():N}", response = "approve" },
            Secret);

        Assert.Equal(404, status);
    }

    [Fact]
    public async Task PendingInteractionsEndpoint_IsAvailable()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

        // The decision-inbox surface (#227) must be reachable for the UI to poll. Asserts the endpoint
        // is wired and returns an array, independent of whether any interaction is currently pending.
        var (status, items) = await Api.ListPendingInteractionsAsync();

        Assert.Equal(200, status);
        Assert.IsAssignableFrom<JsonArray>(items);
    }
}

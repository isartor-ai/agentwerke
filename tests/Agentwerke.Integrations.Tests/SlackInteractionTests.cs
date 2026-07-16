using System.Net;
using System.Security.Cryptography;
using System.Text;
using Agentwerke.Integrations.Webhooks;
using Microsoft.Extensions.Options;

namespace Agentwerke.Integrations.Tests;

public sealed class SlackInteractionTests
{
    private const string Secret = "slack-signing-secret";

    /// <summary>The instant the fixture timestamp represents. Kept fixed so signatures stay stable.</summary>
    private static readonly DateTimeOffset SignedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000);

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private static (string Signature, string Timestamp) Sign(byte[] body)
    {
        const string ts = "1700000000";
        var basestring = Encoding.UTF8.GetBytes($"v0:{ts}:").Concat(body).ToArray();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), basestring);
        return ("v0=" + Convert.ToHexString(hash).ToLowerInvariant(), ts);
    }

    [Fact]
    public void ValidateSlack_ValidSignature_Passes()
    {
        var body = Encoding.UTF8.GetBytes("payload=%7B%7D");
        var (sig, ts) = Sign(body);
        Assert.True(WebhookSignatureValidator.ValidateSlack(body, sig, ts, Secret, SignedAt, Window).IsValid);
    }

    [Fact]
    public void ValidateSlack_TamperedBody_Fails()
    {
        var body = Encoding.UTF8.GetBytes("payload=%7B%7D");
        var (sig, ts) = Sign(body);
        var tampered = Encoding.UTF8.GetBytes("payload=%7B%22x%22%3A1%7D");
        Assert.False(WebhookSignatureValidator.ValidateSlack(tampered, sig, ts, Secret, SignedAt, Window).IsValid);
    }

    [Fact]
    public void ValidateSlack_EmptySecret_SkipsValidation()
    {
        Assert.True(WebhookSignatureValidator.ValidateSlack([1, 2, 3], null, null, string.Empty, SignedAt, Window).IsValid);
    }

    [Fact]
    public void ValidateSlack_MissingHeaders_Fail()
    {
        Assert.False(WebhookSignatureValidator.ValidateSlack([1], null, "1700000000", Secret, SignedAt, Window).IsValid);
        Assert.False(WebhookSignatureValidator.ValidateSlack([1], "v0=abc", null, Secret, SignedAt, Window).IsValid);
    }

    /// <summary>
    /// The gap this closes: the timestamp is signed but was never checked, so a captured payload
    /// replayed days later validated cleanly (#225).
    /// </summary>
    [Fact]
    public void ValidateSlack_StaleTimestamp_FailsEvenWithAValidSignature()
    {
        var body = Encoding.UTF8.GetBytes("payload=%7B%7D");
        var (sig, ts) = Sign(body);

        var result = WebhookSignatureValidator.ValidateSlack(
            body, sig, ts, Secret, SignedAt.AddMinutes(6), Window);

        Assert.False(result.IsValid);
        Assert.Contains("window", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A clock skewed the other way is just as replayable.</summary>
    [Fact]
    public void ValidateSlack_FutureTimestamp_Fails()
    {
        var body = Encoding.UTF8.GetBytes("payload=%7B%7D");
        var (sig, ts) = Sign(body);

        Assert.False(WebhookSignatureValidator.ValidateSlack(
            body, sig, ts, Secret, SignedAt.AddMinutes(-6), Window).IsValid);
    }

    [Fact]
    public void ValidateSlack_WithinTheWindow_Passes()
    {
        var body = Encoding.UTF8.GetBytes("payload=%7B%7D");
        var (sig, ts) = Sign(body);

        Assert.True(WebhookSignatureValidator.ValidateSlack(
            body, sig, ts, Secret, SignedAt.AddMinutes(4), Window).IsValid);
    }

    [Fact]
    public void ValidateSlack_NonNumericTimestamp_Fails()
    {
        Assert.False(WebhookSignatureValidator.ValidateSlack(
            [1], "v0=abc", "not-a-timestamp", Secret, SignedAt, Window).IsValid);
    }

    [Fact]
    public async Task SlackConnector_WithApprovalId_RendersInteractiveButtons()
    {
        var requests = new List<string>();
        var connector = new SlackConnector(
            new HttpClient(new CaptureHandler(requests)),
            Options.Create(new IntegrationOptions
            {
                Slack = new SlackOptions { Enabled = true, WebhookUrl = "https://hooks.slack.test/1" },
            }),
            new StubSecretStore(),
            new AllowAllPolicyEvaluationService(),
            new NoOpAuditRepository(),
            new NoOpWorkflowMetrics(),
            new StubCorrelationContext(),
            new NoOpWorkflowTracer());

        await connector.SendNotificationAsync(
            new SendNotificationCommand("Approval needed", "Review", ApprovalId: "apr_1", RunId: "run_1"));

        var body = Assert.Single(requests);
        Assert.Contains("\"action_id\":\"approve\"", body);
        Assert.Contains("\"action_id\":\"reject\"", body);
        Assert.Contains("apr_1:run_1", body);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly List<string> _bodies;

        public CaptureHandler(List<string> bodies) => _bodies = bodies;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

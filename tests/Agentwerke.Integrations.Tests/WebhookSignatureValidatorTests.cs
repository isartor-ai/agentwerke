using System.Security.Cryptography;
using System.Text;
using Agentwerke.Integrations.Webhooks;

namespace Agentwerke.Integrations.Tests;

public sealed class WebhookSignatureValidatorTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static string HmacSha256Hex(byte[] payload, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── GitHub ────────────────────────────────────────────────────────────────

    [Fact]
    public void GitHub_ValidSignature_ReturnsOk()
    {
        var body = Utf8("""{"action":"opened"}""");
        var secret = "my-secret";
        var sig = HmacSha256Hex(body, secret);

        var result = WebhookSignatureValidator.ValidateGitHub(body, sig, secret);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void GitHub_WrongSignature_ReturnsFail()
    {
        var body = Utf8("""{"action":"opened"}""");
        var result = WebhookSignatureValidator.ValidateGitHub(body, "sha256=deadbeef", "my-secret");

        Assert.False(result.IsValid);
        Assert.Contains("Signature mismatch", result.ErrorMessage);
    }

    [Fact]
    public void GitHub_MissingHeader_ReturnsFail()
    {
        var body = Utf8("{}");
        var result = WebhookSignatureValidator.ValidateGitHub(body, null, "my-secret");

        Assert.False(result.IsValid);
        Assert.Contains("Missing", result.ErrorMessage);
    }

    [Fact]
    public void GitHub_EmptySecret_SkipsValidation()
    {
        // Empty secret = dev mode, always pass.
        var body = Utf8("{}");
        var result = WebhookSignatureValidator.ValidateGitHub(body, null, string.Empty);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void GitHub_BadPrefix_ReturnsFail()
    {
        var body = Utf8("{}");
        var result = WebhookSignatureValidator.ValidateGitHub(body, "md5=abc123", "secret");

        Assert.False(result.IsValid);
        Assert.Contains("sha256=", result.ErrorMessage);
    }

    // ── Jira ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Jira_ValidSignature_ReturnsOk()
    {
        var body = Utf8("""{"webhookEvent":"jira:issue_created"}""");
        var secret = "jira-secret";
        var sig = HmacSha256Hex(body, secret);

        var result = WebhookSignatureValidator.ValidateJira(body, sig, secret);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Jira_EmptySecret_SkipsValidation()
    {
        var result = WebhookSignatureValidator.ValidateJira(Utf8("{}"), null, string.Empty);
        Assert.True(result.IsValid);
    }

    // ── Event ingress (#206) ─────────────────────────────────────────────────

    [Fact]
    public void EventIngress_ValidSignature_ReturnsOk()
    {
        var body = Utf8("""{"messageName":"test.unit.completed"}""");
        var secret = "ci-secret";

        var result = WebhookSignatureValidator.ValidateEventIngress(body, HmacSha256Hex(body, secret), secret);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EventIngress_WrongSignature_ReturnsFail()
    {
        var result = WebhookSignatureValidator.ValidateEventIngress(Utf8("{}"), "sha256=deadbeef", "ci-secret");

        Assert.False(result.IsValid);
        Assert.Contains("Signature mismatch", result.ErrorMessage);
    }

    [Fact]
    public void EventIngress_MissingHeader_ReturnsFail()
    {
        var result = WebhookSignatureValidator.ValidateEventIngress(Utf8("{}"), null, "ci-secret");

        Assert.False(result.IsValid);
        Assert.Contains("Missing X-Agentwerke-Signature-256", result.ErrorMessage);
    }

    /// <summary>
    /// The inverse of <see cref="Jira_EmptySecret_SkipsValidation"/> — and the point of having a
    /// separate method rather than reusing ValidateGitHub. This endpoint resumes waiting runs, so a
    /// source with no secret must be rejected rather than silently trusted.
    /// </summary>
    [Fact]
    public void EventIngress_EmptySecret_FailsClosedInsteadOfSkippingValidation()
    {
        var body = Utf8("{}");

        var result = WebhookSignatureValidator.ValidateEventIngress(body, HmacSha256Hex(body, ""), string.Empty);

        Assert.False(result.IsValid);
        Assert.Contains("no configured secret", result.ErrorMessage);
    }

    [Fact]
    public void EventIngress_SignatureDigest_IsStableForTheSameBodyAndSecret()
    {
        var body = Utf8("""{"messageName":"test.unit.completed","correlationKey":"b1:unit"}""");

        var first = WebhookSignatureValidator.ComputeSignatureDigest(body, "ci-secret");
        var second = WebhookSignatureValidator.ComputeSignatureDigest(body, "ci-secret");

        Assert.Equal(first, second);
        Assert.NotEqual(first, WebhookSignatureValidator.ComputeSignatureDigest(Utf8("{}"), "ci-secret"));
    }
}

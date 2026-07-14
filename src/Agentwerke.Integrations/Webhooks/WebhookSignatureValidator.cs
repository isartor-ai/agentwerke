using System.Security.Cryptography;
using System.Text;

namespace Agentwerke.Integrations.Webhooks;

/// <summary>
/// Validates HMAC-SHA256 webhook signatures.
/// Both Jira and GitHub use the same scheme: HMAC-SHA256 of the raw request body
/// with a shared secret, compared against a header value.
/// </summary>
public static class WebhookSignatureValidator
{
    /// <summary>
    /// Validates a GitHub-style "sha256=&lt;hex&gt;" signature from the X-Hub-Signature-256 header.
    /// </summary>
    public static WebhookValidationResult ValidateGitHub(
        byte[] requestBody,
        string? signatureHeader,
        string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return WebhookValidationResult.Ok(); // secret not configured = skip validation
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return WebhookValidationResult.Fail("Missing X-Hub-Signature-256 header.");
        }

        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return WebhookValidationResult.Fail("Signature header must start with 'sha256='.");
        }

        var expected = ComputeHmacSha256Hex(requestBody, secret);
        var provided = signatureHeader[prefix.Length..];

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(provided))
            ? WebhookValidationResult.Ok()
            : WebhookValidationResult.Fail("Signature mismatch.");
    }

    /// <summary>
    /// Validates a Jira-style signature. Jira Cloud uses a shared secret in
    /// the X-Hub-Signature header (format: "sha256=&lt;hex&gt;").
    /// </summary>
    public static WebhookValidationResult ValidateJira(
        byte[] requestBody,
        string? signatureHeader,
        string secret)
    {
        // Jira and GitHub use the same format — reuse.
        return ValidateGitHub(requestBody, signatureHeader, secret);
    }

    /// <summary>
    /// Validates a generic event-ingress signature from the X-Agentwerke-Signature-256 header
    /// (format: "sha256=&lt;hex&gt;"), for <c>POST /webhooks/events</c> (#206).
    /// </summary>
    /// <remarks>
    /// Deliberately stricter than <see cref="ValidateGitHub"/>: a missing secret fails instead of
    /// skipping validation. The connector webhooks skip-on-empty because an unsigned GitHub payload
    /// can at worst start a run that GitHub was going to trigger anyway, whereas this endpoint
    /// resumes an arbitrary waiting run by correlation key — an unauthenticated caller could
    /// satisfy a verification gate. Misconfiguration must fail closed.
    /// </remarks>
    public static WebhookValidationResult ValidateEventIngress(
        byte[] requestBody,
        string? signatureHeader,
        string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return WebhookValidationResult.Fail("Event ingress source has no configured secret.");
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return WebhookValidationResult.Fail("Missing X-Agentwerke-Signature-256 header.");
        }

        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return WebhookValidationResult.Fail("Signature header must start with 'sha256='.");
        }

        var expected = ComputeHmacSha256Hex(requestBody, secret);
        var provided = signatureHeader[prefix.Length..];

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(provided))
            ? WebhookValidationResult.Ok()
            : WebhookValidationResult.Fail("Signature mismatch.");
    }

    /// <summary>
    /// The HMAC-SHA256 hex digest of a body under a secret. Exposed so the event ingress can
    /// derive a stable idempotency key for senders that deliver no explicit delivery id.
    /// </summary>
    public static string ComputeSignatureDigest(byte[] requestBody, string secret) =>
        ComputeHmacSha256Hex(requestBody, secret);

    /// <summary>
    /// Validates a Slack request signature: <c>X-Slack-Signature</c> is
    /// <c>v0=&lt;hex&gt;</c> where the HMAC-SHA256 is computed over
    /// <c>v0:{X-Slack-Request-Timestamp}:{raw body}</c> with the app signing secret.
    /// </summary>
    public static WebhookValidationResult ValidateSlack(
        byte[] requestBody,
        string? signatureHeader,
        string? timestampHeader,
        string signingSecret)
    {
        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            return WebhookValidationResult.Ok(); // not configured = skip validation
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return WebhookValidationResult.Fail("Missing X-Slack-Signature header.");
        }

        if (string.IsNullOrWhiteSpace(timestampHeader))
        {
            return WebhookValidationResult.Fail("Missing X-Slack-Request-Timestamp header.");
        }

        const string prefix = "v0=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return WebhookValidationResult.Fail("Signature header must start with 'v0='.");
        }

        var prefixBytes = Encoding.UTF8.GetBytes($"v0:{timestampHeader}:");
        var basestring = new byte[prefixBytes.Length + requestBody.Length];
        Buffer.BlockCopy(prefixBytes, 0, basestring, 0, prefixBytes.Length);
        Buffer.BlockCopy(requestBody, 0, basestring, prefixBytes.Length, requestBody.Length);

        var expected = "v0=" + ComputeHmacSha256Hex(basestring, signingSecret);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signatureHeader))
            ? WebhookValidationResult.Ok()
            : WebhookValidationResult.Fail("Signature mismatch.");
    }

    private static string ComputeHmacSha256Hex(byte[] payload, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(key, payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

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
    ///
    /// The timestamp is inside the signed base string, but signing it is not the same as checking it:
    /// until this took <paramref name="now"/>, the timestamp was never compared to anything and a
    /// captured payload stayed replayable forever (#225).
    ///
    /// <paramref name="now"/> is required rather than defaulted. An optional clock would make replay
    /// protection opt-in — the same shape as the "no secret configured = skip validation" branch above,
    /// which is exactly how this gap survived unnoticed. A caller must state its clock.
    /// </summary>
    public static WebhookValidationResult ValidateSlack(
        byte[] requestBody,
        string? signatureHeader,
        string? timestampHeader,
        string signingSecret,
        DateTimeOffset now,
        TimeSpan tolerance)
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

        if (!long.TryParse(timestampHeader, out var unixSeconds))
        {
            return WebhookValidationResult.Fail("X-Slack-Request-Timestamp must be unix seconds.");
        }

        // Freshness before the HMAC: a stale request is refused however well it is signed, which is
        // what bounds the replay window for a captured payload.
        if ((now - DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).Duration() > tolerance)
        {
            return WebhookValidationResult.Fail(
                $"Timestamp is outside the allowed {tolerance.TotalMinutes:0} minute window.");
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

    /// <summary>
    /// Signs an outbound Agentwerke interaction webhook: HMAC-SHA256 over <c>{timestamp}.{raw body}</c>,
    /// returned as the <c>sha256=&lt;hex&gt;</c> value for <c>X-Agentwerke-Signature</c>. The timestamp is
    /// inside the signed material so it cannot be altered to widen the replay window.
    /// </summary>
    public static string SignAgentwerke(byte[] requestBody, string timestamp, string secret) =>
        "sha256=" + ComputeHmacSha256Hex(BuildAgentwerkeBaseString(requestBody, timestamp), secret);

    /// <summary>
    /// Validates an inbound Agentwerke interaction-response signature and its freshness.
    ///
    /// Two deliberate differences from the trigger validators above:
    ///
    /// 1. It <b>fails closed</b> when no secret is configured. Skipping validation is defensible for a
    ///    trigger — the worst case is an unwanted workflow run. This endpoint resumes a parked run and
    ///    decides an agent's confirmation, so an unset secret would be an unauthenticated resume.
    /// 2. It enforces a freshness window. <see cref="ValidateSlack"/> puts the timestamp in the signed
    ///    base string but never checks that it is recent, so a captured payload stays replayable
    ///    forever; that is not repeated here.
    /// </summary>
    public static WebhookValidationResult ValidateAgentwerke(
        byte[] requestBody,
        string? signatureHeader,
        string? timestampHeader,
        string secret,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return WebhookValidationResult.Fail(
                "Interaction webhook secret is not configured; refusing the request.");
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return WebhookValidationResult.Fail("Missing X-Agentwerke-Signature header.");
        }

        if (string.IsNullOrWhiteSpace(timestampHeader))
        {
            return WebhookValidationResult.Fail("Missing X-Agentwerke-Timestamp header.");
        }

        if (!long.TryParse(timestampHeader, out var unixSeconds))
        {
            return WebhookValidationResult.Fail("X-Agentwerke-Timestamp must be unix seconds.");
        }

        // Check freshness before the HMAC: a stale request is rejected regardless of how well it is
        // signed, which is what bounds the replay window for a captured payload.
        var skew = now - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (skew.Duration() > tolerance)
        {
            return WebhookValidationResult.Fail(
                $"Timestamp is outside the allowed {tolerance.TotalMinutes:0} minute window.");
        }

        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return WebhookValidationResult.Fail("Signature header must start with 'sha256='.");
        }

        var expected = SignAgentwerke(requestBody, timestampHeader, secret);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signatureHeader))
            ? WebhookValidationResult.Ok()
            : WebhookValidationResult.Fail("Signature mismatch.");
    }

    private static byte[] BuildAgentwerkeBaseString(byte[] requestBody, string timestamp)
    {
        var prefixBytes = Encoding.UTF8.GetBytes($"{timestamp}.");
        var basestring = new byte[prefixBytes.Length + requestBody.Length];
        Buffer.BlockCopy(prefixBytes, 0, basestring, 0, prefixBytes.Length);
        Buffer.BlockCopy(requestBody, 0, basestring, prefixBytes.Length, requestBody.Length);
        return basestring;
    }

    private static string ComputeHmacSha256Hex(byte[] payload, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(key, payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

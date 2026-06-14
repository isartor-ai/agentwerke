using System.Security.Cryptography;
using System.Text;

namespace Autofac.Integrations.Webhooks;

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

    private static string ComputeHmacSha256Hex(byte[] payload, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(key, payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

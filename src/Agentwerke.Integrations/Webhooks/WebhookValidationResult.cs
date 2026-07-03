namespace Agentwerke.Integrations.Webhooks;

public sealed class WebhookValidationResult
{
    public bool IsValid { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static WebhookValidationResult Ok() => new() { IsValid = true };
    public static WebhookValidationResult Fail(string reason) => new() { IsValid = false, ErrorMessage = reason };
}

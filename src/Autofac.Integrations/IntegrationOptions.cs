namespace Autofac.Integrations;

public sealed class IntegrationOptions
{
    public const string Section = "Integrations";

    public JiraOptions Jira { get; set; } = new();
    public GitHubOptions GitHub { get; set; } = new();
}

public sealed class JiraOptions
{
    /// <summary>
    /// Shared secret used to validate the X-Hub-Signature header.
    /// Leave empty to skip signature validation (development only).
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Jira webhook events that trigger workflow runs.
    /// Defaults to "jira:issue_created".
    /// </summary>
    public List<string> TriggerEvents { get; set; } = ["jira:issue_created"];
}

public sealed class GitHubOptions
{
    /// <summary>
    /// Shared secret for X-Hub-Signature-256 validation.
    /// Leave empty to skip validation.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// GitHub issue actions that trigger workflow runs.
    /// Defaults to "opened".
    /// </summary>
    public List<string> TriggerActions { get; set; } = ["opened"];
}

using System.Text.Json.Serialization;

namespace Autofac.Integrations.Webhooks;

/// <summary>
/// Subset of GitHub's "issues" webhook event payload.
/// GitHub sends this for actions like "opened", "labeled", "closed".
/// </summary>
public sealed class GitHubWebhookPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("issue")]
    public GitHubIssue? Issue { get; set; }

    [JsonPropertyName("repository")]
    public GitHubRepository? Repository { get; set; }

    [JsonPropertyName("sender")]
    public GitHubUser? Sender { get; set; }
}

public sealed class GitHubIssue
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("labels")]
    public List<GitHubLabel>? Labels { get; set; }
}

public sealed class GitHubRepository
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}

public sealed class GitHubLabel
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class GitHubUser
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;
}

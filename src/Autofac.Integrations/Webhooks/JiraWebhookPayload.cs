using System.Text.Json.Serialization;

namespace Autofac.Integrations.Webhooks;

/// <summary>
/// Subset of the Jira issue webhook payload we need for triggering workflows.
/// Jira sends this for events like "issue_created", "issue_updated".
/// </summary>
public sealed class JiraWebhookPayload
{
    [JsonPropertyName("webhookEvent")]
    public string WebhookEvent { get; set; } = string.Empty;

    [JsonPropertyName("issue")]
    public JiraIssue? Issue { get; set; }

    [JsonPropertyName("user")]
    public JiraUser? User { get; set; }
}

public sealed class JiraIssue
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("self")]
    public string? Self { get; set; }

    [JsonPropertyName("fields")]
    public JiraIssueFields? Fields { get; set; }
}

public sealed class JiraIssueFields
{
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("issuetype")]
    public JiraIssueType? IssueType { get; set; }

    [JsonPropertyName("project")]
    public JiraProject? Project { get; set; }
}

public sealed class JiraIssueType
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class JiraProject
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class JiraUser
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("emailAddress")]
    public string? EmailAddress { get; set; }
}

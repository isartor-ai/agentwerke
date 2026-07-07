using System.Text.Json.Serialization;

namespace Agentwerke.Integrations.Webhooks;

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

/// <summary>
/// Subset of GitHub's "issue_comment" webhook event payload.
/// GitHub sends this for issue and pull request comments. Agentwerke uses issue comments
/// containing an approval token to resume GitHub issue approval gates.
/// </summary>
public sealed class GitHubIssueCommentWebhookPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("issue")]
    public GitHubIssue? Issue { get; set; }

    [JsonPropertyName("comment")]
    public GitHubComment? Comment { get; set; }

    [JsonPropertyName("repository")]
    public GitHubRepository? Repository { get; set; }

    [JsonPropertyName("sender")]
    public GitHubUser? Sender { get; set; }
}

public sealed class GitHubComment
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("user")]
    public GitHubUser? User { get; set; }
}

/// <summary>
/// Subset of GitHub's "pull_request" webhook event payload.
/// GitHub sends this for actions like "opened", "synchronize", "closed"
/// (merges show up as action "closed" with pull_request.merged = true).
/// </summary>
public sealed class GitHubPullRequestWebhookPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("pull_request")]
    public GitHubPullRequestWebhookDetails? PullRequest { get; set; }

    [JsonPropertyName("repository")]
    public GitHubRepository? Repository { get; set; }
}

public sealed class GitHubPullRequestWebhookDetails
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("merged")]
    public bool Merged { get; set; }

    [JsonPropertyName("merge_commit_sha")]
    public string? MergeCommitSha { get; set; }

    [JsonPropertyName("head")]
    public GitHubWebhookRef? Head { get; set; }

    [JsonPropertyName("base")]
    public GitHubWebhookRef? Base { get; set; }
}

public sealed class GitHubWebhookRef
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("sha")]
    public string? Sha { get; set; }
}

/// <summary>
/// Subset of GitHub's "workflow_run" webhook event payload.
/// GitHub sends this for actions "requested", "in_progress", "completed".
/// </summary>
public sealed class GitHubWorkflowRunWebhookPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("workflow_run")]
    public GitHubRunStatus? WorkflowRun { get; set; }

    [JsonPropertyName("repository")]
    public GitHubRepository? Repository { get; set; }
}

/// <summary>
/// Subset of GitHub's "check_suite" webhook event payload.
/// GitHub sends this for actions "requested", "rerequested", "completed".
/// </summary>
public sealed class GitHubCheckSuiteWebhookPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("check_suite")]
    public GitHubRunStatus? CheckSuite { get; set; }

    [JsonPropertyName("repository")]
    public GitHubRepository? Repository { get; set; }
}

/// <summary>Shared shape between workflow_run and check_suite payloads — both carry the same status/conclusion/head fields.</summary>
public sealed class GitHubRunStatus
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("conclusion")]
    public string? Conclusion { get; set; }

    [JsonPropertyName("head_sha")]
    public string? HeadSha { get; set; }

    [JsonPropertyName("head_branch")]
    public string? HeadBranch { get; set; }
}

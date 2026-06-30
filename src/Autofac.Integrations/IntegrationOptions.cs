namespace Autofac.Integrations;

public sealed class IntegrationOptions
{
    public const string Section = "Integrations";

    public JiraOptions Jira { get; set; } = new();
    public GitHubOptions GitHub { get; set; } = new();
    public SlackOptions Slack { get; set; } = new();
    public TeamsOptions Teams { get; set; } = new();
    public NotificationOptions Notifications { get; set; } = new();
}

public sealed class NotificationOptions
{
    /// <summary>
    /// Send a Slack/Teams notification when a run enters a human-approval gate.
    /// Enabled by default; per-channel delivery still requires that channel's
    /// connector to be enabled and configured.
    /// </summary>
    public bool OnApprovalRequested { get; set; } = true;
}

public sealed class JiraOptions
{
    public bool Enabled { get; set; }

    public string ApiBaseUrl { get; set; } = "https://your-domain.atlassian.net/";

    public string Username { get; set; } = string.Empty;

    public string ApiToken { get; set; } = string.Empty;

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
    public bool Enabled { get; set; }

    /// <summary>
    /// Base URL for the GitHub REST API.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com/";

    /// <summary>
    /// Shared secret for X-Hub-Signature-256 validation.
    /// Leave empty to skip validation.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Repository owner used for outbound branch / pull request actions.
    /// </summary>
    public string RepositoryOwner { get; set; } = string.Empty;

    /// <summary>
    /// Repository name used for outbound branch / pull request actions.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// Personal access token or GitHub App token used for outbound API calls.
    /// </summary>
    public string PersonalAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Default base branch for Autofac-created branches and pull requests.
    /// </summary>
    public string DefaultBaseBranch { get; set; } = "main";

    /// <summary>
    /// Prefix for deterministic Autofac-created branch names.
    /// </summary>
    public string BranchPrefix { get; set; } = "autofac/run-";

    /// <summary>
    /// Create pull requests as drafts by default for MVP safety.
    /// </summary>
    public bool CreateDraftPullRequests { get; set; } = true;

    /// <summary>
    /// GitHub issue actions that trigger workflow runs.
    /// Defaults to "opened".
    /// </summary>
    public List<string> TriggerActions { get; set; } = ["opened"];

    /// <summary>
    /// Workflow file name (or numeric workflow id) dispatched by the SDLC "deploy to test" gate
    /// (#139) when a tool call doesn't specify one explicitly.
    /// </summary>
    public string DeployWorkflowFileName { get; set; } = "deploy-to-test.yml";
}

public sealed class SlackOptions
{
    public bool Enabled { get; set; }

    public string WebhookUrl { get; set; } = string.Empty;
}

public sealed class TeamsOptions
{
    public bool Enabled { get; set; }

    public string WebhookUrl { get; set; } = string.Empty;
}

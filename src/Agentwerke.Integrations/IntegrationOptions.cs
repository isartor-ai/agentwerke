namespace Agentwerke.Integrations;

public sealed class IntegrationOptions
{
    public const string Section = "Integrations";

    public JiraOptions Jira { get; set; } = new();
    public GitHubOptions GitHub { get; set; } = new();
    public SlackOptions Slack { get; set; } = new();
    public TeamsOptions Teams { get; set; } = new();
    public NotificationOptions Notifications { get; set; } = new();

    /// <summary>
    /// The generic outbound/inbound interaction webhook (#224). Provider-specific, so it lives here
    /// rather than in InteractionOptions: that type is consumed by the provider-neutral router in
    /// Agentwerke.Application, which must know nothing about any particular channel.
    /// </summary>
    public InteractionWebhookOptions InteractionWebhook { get; set; } = new();
    public EventIngressOptions EventIngress { get; set; } = new();
}

public sealed class InteractionWebhookOptions
{
    public bool Enabled { get; set; }

    /// <summary>Absolute URL an interaction is POSTed to.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// HMAC secret for both directions: signing the outbound POST and verifying the inbound response.
    /// Resolved through ISecretStore, so this may hold a secret reference rather than the value.
    ///
    /// Unlike the Jira/GitHub trigger secrets, an empty value here is fatal rather than "skip
    /// verification": the inbound endpoint resumes a parked run, so it fails closed.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>How far an inbound response's timestamp may drift before it is rejected as a replay.</summary>
    public int ToleranceSeconds { get; set; } = 300;
}

/// <summary>
/// Generic signed event ingress (#206). Lets any CI or test system deliver a domain event
/// (<c>test.unit.completed</c>, <c>deploy.staging.succeeded</c>, …) to <c>POST /webhooks/events</c>
/// without Agentwerke needing a first-class connector for it, keeping BPMN message names
/// decoupled from GitHub's event taxonomy.
/// </summary>
public sealed class EventIngressOptions
{
    /// <summary>
    /// Disabled by default: the endpoint can resume runs, so it must be turned on deliberately.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Registered senders, keyed by the value each sends in the X-Agentwerke-Source header.
    /// A request naming an unregistered source is rejected — there is no anonymous ingress.
    /// </summary>
    public List<EventIngressSourceOptions> Sources { get; set; } = [];
}

public sealed class EventIngressSourceOptions
{
    /// <summary>Source identifier the sender presents in the X-Agentwerke-Source header.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret for this source's HMAC-SHA256 signature. Unlike the connector webhooks,
    /// an empty secret does not skip validation — it disables the source (see
    /// <c>WebhookSignatureValidator.ValidateEventIngress</c>).
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Optional allowlist of message names this source may deliver. Empty means any message name.
    /// Narrow this in production so a compromised CI token cannot resume unrelated waits.
    /// </summary>
    public List<string> AllowedMessageNames { get; set; } = [];
}

public sealed class NotificationOptions
{
    /// <summary>
    /// Send a Slack/Teams notification when a run enters a human-approval gate.
    /// Enabled by default; per-channel delivery still requires that channel's
    /// connector to be enabled and configured.
    /// </summary>
    public bool OnApprovalRequested { get; set; } = true;

    /// <summary>
    /// Render interactive Approve/Reject buttons on Slack approval notifications so an
    /// approver can decide from chat (#172). Requires Slack:SigningSecret + a Slack app
    /// with an interactivity request URL pointing at /webhooks/slack/interactions.
    /// </summary>
    public bool Interactive { get; set; } = false;
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
    /// Default base branch for Agentwerke-created branches and pull requests.
    /// </summary>
    public string DefaultBaseBranch { get; set; } = "main";

    /// <summary>
    /// Prefix for deterministic Agentwerke-created branch names.
    /// </summary>
    public string BranchPrefix { get; set; } = "agentwerke/run-";

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
    /// Label an issue must carry (case-insensitive) for its webhook to start a run.
    /// Prevents every issue opened on the configured repo from spending model budget.
    /// Set to empty/whitespace to disable the check and trigger on any matching action.
    /// </summary>
    public string RequiredLabel { get; set; } = "agentwerke";

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

    /// <summary>
    /// Slack app signing secret used to verify inbound interaction callbacks
    /// (approve/reject from a message). Leave empty to skip verification (dev only). #172
    /// </summary>
    public string SigningSecret { get; set; } = string.Empty;

    /// <summary>
    /// Bot token (xoxb-…), required only for free-text answers, which need views.open to raise a
    /// modal. An incoming webhook alone cannot open one, so without this the Slack channel degrades
    /// to structured choices plus a link back to Agentwerke (#225).
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// How far an inbound Slack request's timestamp may drift before it is rejected as a replay.
    ///
    /// Slack signs "v0:{timestamp}:{body}" but nothing forces the timestamp to be recent, so without
    /// this window a captured payload stays replayable forever — Slack's own guidance is five
    /// minutes (#225).
    /// </summary>
    public int ToleranceSeconds { get; set; } = 300;
}

public sealed class TeamsOptions
{
    public bool Enabled { get; set; }

    public string WebhookUrl { get; set; } = string.Empty;
}

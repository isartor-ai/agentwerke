using Autofac.Integrations;
using Autofac.Domain.AgentRuntime;
using Autofac.Sandboxes;
using Autofac.Workflows.Runtime;
using System.Globalization;
using System.Text.Json;

namespace Autofac.Agents.Tools;

public sealed class GitHubCreateBranchTool : IAgentTool, IToolSchemaProvider
{
    private readonly IGitHubConnector _connector;

    public GitHubCreateBranchTool(IGitHubConnector connector)
    {
        _connector = connector;
    }

    public string Name => "github.create_branch";

    public string Category => AgentToolCategories.Integration;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("branch_name", "string", "The name of the branch to create.", Required: true),
        new("base_branch", "string", "The base branch to create from. Defaults to the repository default branch.", Required: false)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        Require(input, "branch_name");
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var branch = await _connector.CreateBranchAsync(
            new CreateGitHubBranchCommand(
                BranchName: input["branch_name"],
                BaseBranch: ReadOptional(input, "base_branch")),
            cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: $"""
                provider: github
                action: create_branch
                status: completed
                branch: {branch.BranchName}
                base: {branch.BaseBranch}
                url: {branch.BranchUrl}
                sha: {branch.CommitSha}
                existing: {branch.AlreadyExisted}
                """,
            FailureReason: null,
            ExternalActions:
            [
                new ExternalActionRecord(
                    Provider: "github",
                    Action: "create_branch",
                    Status: branch.AlreadyExisted ? "already_exists" : "completed",
                    ResourceId: branch.BranchName,
                    ResourceUrl: branch.BranchUrl,
                    Summary: $"GitHub branch {branch.BranchName}")
            ]);
    }

    private static void Require(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Tool input is missing required field '{key}'.");
        }
    }

    private static string? ReadOptional(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

public sealed class GitHubCreatePullRequestTool : IAgentTool, IToolSchemaProvider
{
    private readonly IGitHubConnector _connector;

    public GitHubCreatePullRequestTool(IGitHubConnector connector)
    {
        _connector = connector;
    }

    public string Name => "github.create_pull_request";

    public string Category => AgentToolCategories.Integration;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("head_branch", "string", "The source branch to merge from.", Required: true),
        new("title", "string", "The pull request title.", Required: true),
        new("body", "string", "The pull request description body.", Required: true),
        new("commit_message", "string", "Commit message for evidence commits.", Required: true),
        new("run_id", "string", "The workflow run ID (injected automatically).", Required: false),
        new("step_id", "string", "The workflow step ID (injected automatically).", Required: false),
        new("attempt", "string", "The execution attempt number (injected automatically).", Required: false)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        Require(input, "run_id");
        Require(input, "step_id");
        Require(input, "attempt");
        Require(input, "head_branch");
        Require(input, "title");
        Require(input, "body");
        Require(input, "commit_message");
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var ensuredBranch = await _connector.CreateBranchAsync(
            new CreateGitHubBranchCommand(
                BranchName: input["head_branch"],
                BaseBranch: ReadOptional(input, "base_branch")),
            cancellationToken);

        var pullRequest = await _connector.CreatePullRequestAsync(
            new CreateGitHubPullRequestCommand(
                RunId: input["run_id"],
                StepId: input["step_id"],
                Attempt: int.Parse(input["attempt"], System.Globalization.CultureInfo.InvariantCulture),
                HeadBranch: input["head_branch"],
                BaseBranch: ReadOptional(input, "base_branch"),
                Title: input["title"],
                Body: input["body"],
                CommitMessage: input["commit_message"]),
            cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: $"""
                provider: github
                action: create_pull_request
                status: completed
                branch: {ensuredBranch.BranchName}
                branch_existing: {ensuredBranch.AlreadyExisted}
                pull_request: #{pullRequest.Number}
                url: {pullRequest.PullRequestUrl}
                marker: {pullRequest.MarkerPath}
                existing: {pullRequest.AlreadyExisted}
                """,
            FailureReason: null,
            ExternalActions:
            [
                new ExternalActionRecord(
                    Provider: "github",
                    Action: "create_branch",
                    Status: ensuredBranch.AlreadyExisted ? "already_exists" : "completed",
                    ResourceId: ensuredBranch.BranchName,
                    ResourceUrl: ensuredBranch.BranchUrl,
                    Summary: $"GitHub branch {ensuredBranch.BranchName}"),
                new ExternalActionRecord(
                    Provider: "github",
                    Action: "create_pull_request",
                    Status: pullRequest.AlreadyExisted ? "already_exists" : "completed",
                    ResourceId: pullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ResourceUrl: pullRequest.PullRequestUrl,
                    Summary: $"GitHub pull request #{pullRequest.Number}")
            ]);
    }

    private static void Require(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Tool input is missing required field '{key}'.");
        }
    }

    private static string? ReadOptional(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

public sealed class GitHubReadIssueTool : IAgentTool, IToolSchemaProvider
{
    private readonly IGitHubConnector _connector;

    public GitHubReadIssueTool(IGitHubConnector connector)
    {
        _connector = connector;
    }

    public string Name => "github.read_issue";

    public string Category => AgentToolCategories.Integration;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("issue_number", "string", "The GitHub issue number to load.", Required: true)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        GitHubToolInput.Require(input, "issue_number");
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var issue = await _connector.GetIssueAsync(GitHubToolInput.ParseInt(input, "issue_number"), cancellationToken);
        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: JsonSerializer.Serialize(new
            {
                provider = "github",
                action = "read_issue",
                status = "completed",
                issue_number = issue.Number,
                title = issue.Title,
                body = issue.Body,
                labels = issue.Labels,
                state = issue.State,
                url = issue.IssueUrl,
                comments = issue.Comments
            }),
            FailureReason: null,
            ExternalActions:
            [
                new ExternalActionRecord(
                    Provider: "github",
                    Action: "read_issue",
                    Status: "completed",
                    ResourceId: issue.Number.ToString(CultureInfo.InvariantCulture),
                    ResourceUrl: issue.IssueUrl,
                    Summary: $"GitHub issue #{issue.Number}")
            ]);
    }
}

public sealed class GitHubRequestReviewTool : IAgentTool, IToolSchemaProvider
{
    private readonly IGitHubConnector _connector;

    public GitHubRequestReviewTool(IGitHubConnector connector)
    {
        _connector = connector;
    }

    public string Name => "github.request_review";

    public string Category => AgentToolCategories.Integration;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("pull_number", "string", "The GitHub pull request number.", Required: true),
        new("reviewers", "string", "Comma-separated GitHub usernames to request review from.", Required: true)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        GitHubToolInput.Require(input, "pull_number");
        GitHubToolInput.Require(input, "reviewers");
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var result = await _connector.RequestReviewersAsync(
            new RequestGitHubReviewersCommand(
                GitHubToolInput.ParseInt(input, "pull_number"),
                GitHubToolInput.SplitCsv(input["reviewers"])),
            cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: JsonSerializer.Serialize(new
            {
                provider = "github",
                action = "request_review",
                status = "completed",
                pull_number = result.PullNumber,
                reviewers = result.RequestedReviewers,
                url = result.PullRequestUrl
            }),
            FailureReason: null,
            ExternalActions:
            [
                new ExternalActionRecord(
                    Provider: "github",
                    Action: "request_review",
                    Status: "completed",
                    ResourceId: result.PullNumber.ToString(CultureInfo.InvariantCulture),
                    ResourceUrl: result.PullRequestUrl,
                    Summary: $"Requested GitHub reviewer(s) for pull request #{result.PullNumber}")
            ]);
    }
}

public sealed class GitHubPostReviewTool : IAgentTool, IToolSchemaProvider
{
    private readonly IGitHubConnector _connector;

    public GitHubPostReviewTool(IGitHubConnector connector)
    {
        _connector = connector;
    }

    public string Name => "github.post_review";

    public string Category => AgentToolCategories.Integration;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("pull_number", "string", "The GitHub pull request number.", Required: true),
        new("body", "string", "The review body to post.", Required: true),
        new("event", "string", "The GitHub review event. Defaults to COMMENT.", Required: false)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        GitHubToolInput.Require(input, "pull_number");
        GitHubToolInput.Require(input, "body");
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var result = await _connector.PostReviewAsync(
            new PostGitHubReviewCommand(
                GitHubToolInput.ParseInt(input, "pull_number"),
                input["body"],
                GitHubToolInput.ReadOptional(input, "event") ?? "COMMENT"),
            cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: JsonSerializer.Serialize(new
            {
                provider = "github",
                action = "post_review",
                status = "completed",
                pull_number = result.PullNumber,
                review_id = result.ReviewId,
                @event = result.Event,
                state = result.State,
                url = result.ReviewUrl
            }),
            FailureReason: null,
            ExternalActions:
            [
                new ExternalActionRecord(
                    Provider: "github",
                    Action: "post_review",
                    Status: "completed",
                    ResourceId: result.ReviewId.ToString(CultureInfo.InvariantCulture),
                    ResourceUrl: result.ReviewUrl,
                    Summary: $"Posted GitHub review on pull request #{result.PullNumber}")
            ]);
    }
}

/// <summary>
/// Triggers a CI/CD deploy run (#139). Named under the provider-agnostic "cicd." namespace
/// rather than "github." since a future connector swap (Phase E5, #87) shouldn't rename the
/// tool agents call — only GitHub Actions is wired up today via <see cref="IGitHubConnector"/>.
/// </summary>
public sealed class CicdTriggerDeployTool : IAgentTool, IToolSchemaProvider
{
    private readonly IGitHubConnector _connector;

    public CicdTriggerDeployTool(IGitHubConnector connector)
    {
        _connector = connector;
    }

    public string Name => "cicd.trigger_deploy";

    public string Category => AgentToolCategories.Integration;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("ref", "string", "The commit sha or branch to deploy. Defaults to the repository default branch.", Required: false),
        new("workflow_file", "string", "The Actions workflow file name to dispatch. Defaults to the configured deploy workflow.", Required: false)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        // Both fields are optional — the connector falls back to configured defaults.
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var @ref = GitHubToolInput.ReadOptional(input, "ref");
        var result = await _connector.TriggerWorkflowDispatchAsync(
            new TriggerGitHubWorkflowDispatchCommand(
                WorkflowFileName: GitHubToolInput.ReadOptional(input, "workflow_file"),
                Ref: @ref,
                Inputs: @ref is null ? null : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["sha"] = @ref }),
            cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: JsonSerializer.Serialize(new
            {
                provider = "github",
                action = "trigger_workflow_dispatch",
                status = "dispatched",
                workflow_file = result.WorkflowFileName,
                @ref = result.Ref,
                triggered_at = result.TriggeredAt
            }),
            FailureReason: null,
            ExternalActions:
            [
                new ExternalActionRecord(
                    Provider: "github",
                    Action: "trigger_workflow_dispatch",
                    Status: "dispatched",
                    ResourceId: result.WorkflowFileName,
                    ResourceUrl: null,
                    Summary: $"Dispatched GitHub workflow '{result.WorkflowFileName}' on ref '{result.Ref}'")
            ]);
    }
}

internal static class GitHubToolInput
{
    public static void Require(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Tool input is missing required field '{key}'.");
        }
    }

    public static string? ReadOptional(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static int ParseInt(IReadOnlyDictionary<string, string> input, string key)
    {
        Require(input, key);
        return int.TryParse(input[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"Tool input field '{key}' must be an integer.");
    }

    public static IReadOnlyList<string> SplitCsv(string value) =>
        value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed class SandboxExecutionTool : IAgentTool
{
    private readonly ISandboxExecutor _sandboxExecutor;

    public SandboxExecutionTool(ISandboxExecutor sandboxExecutor)
    {
        _sandboxExecutor = sandboxExecutor;
    }

    public string Name => "sandbox.execute";

    public string Category => AgentToolCategories.Shell;

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        Require(input, "agent_name");
        Require(input, "action");
        Require(input, "purpose_type");
        Require(input, "policy_tag");
        Require(input, "attempt");
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        // ToolGateway has already authorized the requested profile and written the
        // selected name (and its rationale) back into the input before dispatching here.
        var profileName = ReadOptional(input, "sandbox_profile") ?? SandboxProfileCatalog.Default;
        var resolvedProfile = SandboxProfileCatalog.Resolve(profileName, context.RunId);

        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["autofac.sandboxProfile"] = profileName
        };
        var rationale = ReadOptional(input, "sandbox_profile_rationale");
        if (rationale is not null)
        {
            diagnostics["autofac.sandboxProfileRationale"] = rationale;
        }

        var result = await _sandboxExecutor.ExecuteAsync(
            new SandboxExecutionRequest(
                RunId: context.RunId,
                StepId: context.StepId,
                AgentName: input["agent_name"],
                Action: input["action"],
                Environment: ReadOptional(input, "environment"),
                PurposeType: input["purpose_type"],
                PolicyTag: input["policy_tag"],
                Attempt: int.Parse(input["attempt"], System.Globalization.CultureInfo.InvariantCulture),
                Profile: resolvedProfile,
                Metadata: diagnostics),
            cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: result.Succeeded,
            Output: result.Logs,
            FailureReason: result.FailureReason,
            Artifacts: result.Artifacts,
            SandboxExecution: new AgentSandboxExecutionRecord
            {
                Provider = result.Provider.ToConfigValue(),
                SandboxId = result.ProviderSandboxId,
                CommandState = result.CommandState.ToString(),
                ExitCode = result.ExitCode,
                DurationMs = (int)Math.Round(result.Duration.TotalMilliseconds),
                Logs = (result.StructuredLogs ?? [])
                    .Select(static entry => new AgentSandboxLogRecord
                    {
                        Stream = entry.Stream,
                        Message = entry.Message,
                        Timestamp = entry.Timestamp.ToString("o")
                    })
                    .ToArray(),
                Diagnostics = result.ProviderDiagnostics ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            });
    }

    private static void Require(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Tool input is missing required field '{key}'.");
        }
    }

    private static string? ReadOptional(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

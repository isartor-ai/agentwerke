using Agentwerke.Integrations;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Sandboxes;
using Agentwerke.Workflows.Runtime;
using System.Globalization;
using System.Text.Json;

namespace Agentwerke.Agents.Tools;

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

public sealed class GitHubCommentIssueTool : IAgentTool, IToolSchemaProvider
{
    private readonly IGitHubConnector _connector;

    public GitHubCommentIssueTool(IGitHubConnector connector)
    {
        _connector = connector;
    }

    public string Name => "github.comment_issue";

    public string Category => AgentToolCategories.Integration;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("issue_number", "string", "The GitHub issue number to comment on.", Required: true),
        new("body", "string", "The Markdown comment body.", Required: true)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        GitHubToolInput.Require(input, "issue_number");
        GitHubToolInput.Require(input, "body");
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var result = await _connector.CommentIssueAsync(
            new CommentGitHubIssueCommand(
                GitHubToolInput.ParseInt(input, "issue_number"),
                input["body"]),
            cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: JsonSerializer.Serialize(new
            {
                provider = "github",
                action = "comment_issue",
                status = "completed",
                issue_number = result.IssueNumber,
                comment_id = result.CommentId,
                url = result.CommentUrl
            }),
            FailureReason: null,
            ExternalActions:
            [
                new ExternalActionRecord(
                    Provider: "github",
                    Action: "comment_issue",
                    Status: "completed",
                    ResourceId: result.CommentId.ToString(CultureInfo.InvariantCulture),
                    ResourceUrl: result.CommentUrl,
                    Summary: $"Commented on GitHub issue #{result.IssueNumber}")
            ]);
    }
}

public sealed class GitHubCloseIssueTool : IAgentTool, IToolSchemaProvider
{
    private readonly IGitHubConnector _connector;

    public GitHubCloseIssueTool(IGitHubConnector connector)
    {
        _connector = connector;
    }

    public string Name => "github.close_issue";

    public string Category => AgentToolCategories.Integration;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("issue_number", "string", "The GitHub issue number to close.", Required: true),
        new("state_reason", "string", "Optional GitHub issue close reason. Defaults to completed.", Required: false)
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
        var result = await _connector.CloseIssueAsync(
            new CloseGitHubIssueCommand(
                GitHubToolInput.ParseInt(input, "issue_number"),
                GitHubToolInput.ReadOptional(input, "state_reason")),
            cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: JsonSerializer.Serialize(new
            {
                provider = "github",
                action = "close_issue",
                status = result.State,
                issue_number = result.Number,
                url = result.IssueUrl
            }),
            FailureReason: null,
            ExternalActions:
            [
                new ExternalActionRecord(
                    Provider: "github",
                    Action: "close_issue",
                    Status: result.State,
                    ResourceId: result.Number.ToString(CultureInfo.InvariantCulture),
                    ResourceUrl: result.IssueUrl,
                    Summary: $"Closed GitHub issue #{result.Number}")
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
/// <remarks>
/// With <c>correlate</c>, the dispatch carries correlation inputs so the run that fires it can be
/// resumed by the CI job's callback (#210). GitHub's workflow_dispatch API answers 204 with no run
/// id, so there is no external identifier to correlate on afterwards: the workflow must echo back a
/// key Agentwerke chose up front. That key defaults to the run id, which both this tool and a
/// waiting node can derive independently — the node templates <c>{{run_id}}</c> — so neither has to
/// guess it the way <c>{{input.build_id}}</c> did, and no data has to pass between them.
///
/// Correlation is opt-in because workflow_dispatch rejects the whole call with 422 "Unexpected
/// inputs provided" when handed an input the workflow does not declare. Sending the agentwerke_*
/// inputs unconditionally would break every deploy workflow written against #139, which declares
/// at most "sha". Set correlate only when the target workflow declares the inputs below.
/// </remarks>
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
        new("workflow_file", "string", "The Actions workflow file name to dispatch. Defaults to the configured deploy workflow.", Required: false),
        new("correlate", "string", "\"true\" to send the agentwerke_* correlation inputs. Only for workflows that declare them — GitHub rejects the dispatch otherwise.", Required: false),
        new("requirement_id", "string", "Requirement this build verifies, passed to the workflow for traceability. Requires correlate.", Required: false),
        new("correlation_key", "string", "Key the workflow must echo back when reporting its result. Defaults to the run id. Requires correlate.", Required: false)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        // Every field is optional — the connector falls back to configured defaults, and the
        // correlation key falls back to the run id.
        if (!ShouldCorrelate(input)
            && (GitHubToolInput.ReadOptional(input, "correlation_key") is not null
                || GitHubToolInput.ReadOptional(input, "requirement_id") is not null))
        {
            // Otherwise the dispatch looks correlated and silently isn't: the workflow never receives
            // the key, so the callback can never resume the run and the wait times out with no clue.
            throw new InvalidOperationException(
                "Tool input 'correlation_key'/'requirement_id' requires 'correlate' to be \"true\".");
        }
    }

    private static bool ShouldCorrelate(IReadOnlyDictionary<string, string> input) =>
        string.Equals(GitHubToolInput.ReadOptional(input, "correlate"), "true", StringComparison.OrdinalIgnoreCase);

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        Validate(input);

        var @ref = GitHubToolInput.ReadOptional(input, "ref");
        var correlate = ShouldCorrelate(input);
        var requirementId = correlate ? GitHubToolInput.ReadOptional(input, "requirement_id") : null;

        // Defaulted rather than required: this tool is called by an agent, and a correlation key the
        // model invented would not match what the waiting node computed. The run id is the one value
        // both sides already agree on.
        var correlationKey = correlate
            ? GitHubToolInput.ReadOptional(input, "correlation_key") ?? context.RunId
            : null;

        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (@ref is not null)
        {
            // "sha" predates the correlation inputs and is what existing deploy workflows read (#139).
            inputs["sha"] = @ref;
        }

        if (correlate)
        {
            inputs["agentwerke_run_id"] = context.RunId;
            inputs["agentwerke_correlation_key"] = correlationKey!;

            if (@ref is not null)
            {
                inputs["commit_sha"] = @ref;
            }

            if (requirementId is not null)
            {
                inputs["agentwerke_requirement_id"] = requirementId;
            }
        }

        var result = await _connector.TriggerWorkflowDispatchAsync(
            new TriggerGitHubWorkflowDispatchCommand(
                WorkflowFileName: GitHubToolInput.ReadOptional(input, "workflow_file"),
                Ref: @ref,
                Inputs: inputs),
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
                triggered_at = result.TriggeredAt,
                correlation_key = correlationKey,
                requirement_id = requirementId,
                run_id = context.RunId
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
                    Summary: correlationKey is null
                        ? $"Dispatched GitHub workflow '{result.WorkflowFileName}' on ref '{result.Ref}'."
                        : $"Dispatched GitHub workflow '{result.WorkflowFileName}' on ref '{result.Ref}' with correlation key '{correlationKey}'.",
                    CorrelationKey: correlationKey)
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
        var raw = input[key].Trim();
        if (raw.StartsWith('#'))
        {
            raw = raw[1..].Trim();
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
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
            ["agentwerke.sandboxProfile"] = profileName
        };
        var rationale = ReadOptional(input, "sandbox_profile_rationale");
        if (rationale is not null)
        {
            diagnostics["agentwerke.sandboxProfileRationale"] = rationale;
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

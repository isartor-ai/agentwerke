using Autofac.Integrations;
using Autofac.Domain.AgentRuntime;
using Autofac.Sandboxes;
using Autofac.Workflows.Runtime;

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
        var result = await _sandboxExecutor.ExecuteAsync(
            new SandboxExecutionRequest(
                RunId: context.RunId,
                StepId: context.StepId,
                AgentName: input["agent_name"],
                Action: input["action"],
                Environment: ReadOptional(input, "environment"),
                PurposeType: input["purpose_type"],
                PolicyTag: input["policy_tag"],
                Attempt: int.Parse(input["attempt"], System.Globalization.CultureInfo.InvariantCulture)),
            cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: result.Succeeded,
            Output: result.Logs,
            FailureReason: result.FailureReason,
            Artifacts: result.Artifacts);
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

using Autofac.Agents.Skills;
using Autofac.Integrations;
using Autofac.Sandboxes;
using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.Options;

namespace Autofac.Agents;

/// <summary>
/// Bridges BPMN service-task execution to the agent layer.
/// When <see cref="SandboxOptions.Enabled"/> is true, dispatches to
/// <see cref="ISandboxExecutor"/> for Docker-isolated execution.
/// </summary>
public sealed class AgentOrchestrator : IServiceTaskExecutor
{
    private readonly ISkillRepository _skillRepository;
    private readonly IGitHubConnector _gitHubConnector;
    private readonly string _gitHubBranchPrefix;
    private readonly ISandboxExecutor _sandbox;
    private readonly SandboxOptions _sandboxOptions;

    public AgentOrchestrator(
        ISkillRepository skillRepository,
        IGitHubConnector gitHubConnector,
        ISandboxExecutor sandbox,
        IOptions<SandboxOptions> sandboxOptions,
        IOptions<IntegrationOptions> integrationOptions)
    {
        _skillRepository = skillRepository;
        _gitHubConnector = gitHubConnector;
        _sandbox = sandbox;
        _sandboxOptions = sandboxOptions.Value;
        _gitHubBranchPrefix = string.IsNullOrWhiteSpace(integrationOptions.Value.GitHub.BranchPrefix)
            ? "autofac/run-"
            : integrationOptions.Value.GitHub.BranchPrefix;
    }

    public async Task<AgentTaskOutcome> ExecuteAsync(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        int attempt,
        CancellationToken cancellationToken)
    {
        var metadata = node.Metadata;
        if (metadata is null)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: "Service task is missing autofac:agentTask metadata.");
        }

        // Honour the BPMN test-scenario flags so existing fixtures keep working.
        if (attempt <= metadata.FailUntilAttempt)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: $"Simulated failure on attempt {attempt} (failUntilAttempt={metadata.FailUntilAttempt})");
        }

        var profile = AgentRegistry.Find(metadata.Agent);
        var matchedSkillRef = ResolveSkillRef(profile, metadata.Action);
        var skillManifest = matchedSkillRef?.SkillManifestId is not null
            ? _skillRepository.FindById(matchedSkillRef.SkillManifestId)
            : null;

        if (IsGitHubAction(metadata.Action))
        {
            return await RunGitHubActionAsync(runId, stepId, node, metadata, attempt, cancellationToken);
        }

        if (_sandboxOptions.Enabled)
        {
            return await RunInSandboxAsync(runId, stepId, metadata, attempt, skillManifest, cancellationToken);
        }

        var request = new AgentExecutionRequest(
            RunId: runId,
            StepId: stepId,
            NodeId: node.Id,
            NodeName: node.Name,
            AgentName: metadata.Agent,
            Action: metadata.Action,
            Environment: metadata.Environment,
            PurposeType: metadata.PurposeType,
            PolicyTag: metadata.PolicyTag,
            RequiresEvidence: metadata.RequiresEvidence,
            Attempt: attempt);

        var output = BuildSimulatedOutput(request, profile, matchedSkillRef, skillManifest);

        return new AgentTaskOutcome(
            Succeeded: true,
            Output: output,
            FailureReason: null);
    }

    private async Task<AgentTaskOutcome> RunGitHubActionAsync(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        AutofacTaskMetadata metadata,
        int attempt,
        CancellationToken cancellationToken)
    {
        var branchName = BuildBranchName(runId, _gitHubBranchPrefix);

        try
        {
            if (string.Equals(metadata.Action, "github.create_branch", StringComparison.OrdinalIgnoreCase))
            {
                var branch = await _gitHubConnector.CreateBranchAsync(
                    new CreateGitHubBranchCommand(branchName, BaseBranch: null),
                    cancellationToken);

                return new AgentTaskOutcome(
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

            var ensuredBranch = await _gitHubConnector.CreateBranchAsync(
                new CreateGitHubBranchCommand(branchName, BaseBranch: null),
                cancellationToken);

            var title = node.Name is { Length: > 0 }
                ? $"Autofac: {node.Name}"
                : $"Autofac run {runId}";
            var body = BuildPullRequestBody(runId, stepId, metadata, attempt);
            var pullRequest = await _gitHubConnector.CreatePullRequestAsync(
                new CreateGitHubPullRequestCommand(
                    RunId: runId,
                    StepId: stepId,
                    Attempt: attempt,
                    HeadBranch: branchName,
                    BaseBranch: null,
                    Title: title,
                    Body: body,
                    CommitMessage: $"Autofac evidence for run {runId}, step {stepId}"),
                cancellationToken);

            return new AgentTaskOutcome(
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
                        ResourceId: pullRequest.Number.ToString(),
                        ResourceUrl: pullRequest.PullRequestUrl,
                        Summary: $"GitHub pull request #{pullRequest.Number}")
                ]);
        }
        catch (Exception ex)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: ex.Message,
                ExternalActions:
                [
                    new ExternalActionRecord(
                        Provider: "github",
                        Action: metadata.Action,
                        Status: "failed",
                        ResourceId: branchName,
                        ResourceUrl: null,
                        Summary: ex.Message)
                ]);
        }
    }

    private async Task<AgentTaskOutcome> RunInSandboxAsync(
        string runId,
        string stepId,
        AutofacTaskMetadata metadata,
        int attempt,
        SkillManifest? skillManifest,
        CancellationToken cancellationToken)
    {
        var sandboxRequest = new SandboxExecutionRequest(
            RunId: runId,
            StepId: stepId,
            AgentName: metadata.Agent,
            Action: metadata.Action,
            Environment: metadata.Environment,
            PurposeType: metadata.PurposeType,
            PolicyTag: metadata.PolicyTag,
            Attempt: attempt);

        var result = await _sandbox.ExecuteAsync(sandboxRequest, cancellationToken);

        if (!result.Succeeded)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: result.Logs,
                FailureReason: result.FailureReason,
                Artifacts: result.Artifacts);
        }

        var output = BuildSandboxOutput(sandboxRequest, result, skillManifest);

        return new AgentTaskOutcome(
            Succeeded: true,
            Output: output,
            FailureReason: null,
            Artifacts: result.Artifacts);
    }

    private static AgentSkillRef? ResolveSkillRef(AgentProfile? profile, string action)
    {
        if (profile is null) return null;

        return profile.Skills.FirstOrDefault(s =>
            s.SupportedActions.Contains(action, StringComparer.OrdinalIgnoreCase))
            ?? profile.Skills.FirstOrDefault();
    }

    private static bool IsGitHubAction(string action) =>
        string.Equals(action, "github.create_branch", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(action, "github.create_pull_request", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(action, "github.create_pr", StringComparison.OrdinalIgnoreCase);

    private static string BuildBranchName(string runId, string branchPrefix)
    {
        var normalized = new string(runId
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');

        return $"{branchPrefix}{normalized}";
    }

    private static string BuildPullRequestBody(
        string runId,
        string stepId,
        AutofacTaskMetadata metadata,
        int attempt)
    {
        var evidence = metadata.RequiresEvidence.Count == 0
            ? "- none declared"
            : string.Join('\n', metadata.RequiresEvidence.Select(static item => $"- {item}"));

        return $"""
            Generated by Autofac.

            - run: {runId}
            - step: {stepId}
            - agent: {metadata.Agent}
            - action: {metadata.Action}
            - environment: {metadata.Environment ?? "unspecified"}
            - purpose: {metadata.PurposeType}
            - policy: {metadata.PolicyTag}
            - attempt: {attempt}

            Evidence requirements:
            {evidence}
            """;
    }

    private static string BuildSimulatedOutput(
        AgentExecutionRequest request,
        AgentProfile? profile,
        AgentSkillRef? skillRef,
        SkillManifest? manifest)
    {
        var skillLine = manifest is not null
            ? $"{manifest.Name} (id={manifest.SkillId} fingerprint={manifest.Fingerprint[..12]}…)"
            : skillRef?.Name ?? request.Action;

        var contextSection = manifest is not null
            ? $"""

              skill_context:
                id: {manifest.SkillId}
                name: {manifest.Name}
                fingerprint: {manifest.Fingerprint}
                source: {manifest.FilePath}
              """
            : string.Empty;

        return $"""
            agent: {request.AgentName}
            skill: {skillLine}
            action: {request.Action}
            environment: {request.Environment ?? "unspecified"}
            purpose: {request.PurposeType}
            policy: {request.PolicyTag}
            attempt: {request.Attempt}
            status: completed
            mode: simulated
            timestamp: {DateTimeOffset.UtcNow:o}{contextSection}
            """;
    }

    private static string BuildSandboxOutput(
        SandboxExecutionRequest request,
        SandboxExecutionResult result,
        SkillManifest? manifest)
    {
        var skillContext = manifest is not null
            ? $"""

              skill_context:
                id: {manifest.SkillId}
                name: {manifest.Name}
                fingerprint: {manifest.Fingerprint}
              """
            : string.Empty;

        return $"""
            agent: {request.AgentName}
            action: {request.Action}
            environment: {request.Environment ?? "unspecified"}
            purpose: {request.PurposeType}
            policy: {request.PolicyTag}
            attempt: {request.Attempt}
            status: completed
            mode: sandbox
            exit_code: {result.ExitCode}
            duration_ms: {result.Duration.TotalMilliseconds:F0}
            artifact_count: {result.Artifacts.Count}
            timestamp: {DateTimeOffset.UtcNow:o}{skillContext}
            logs: |
            {IndentLogs(result.Logs)}
            """;
    }

    private static string IndentLogs(string logs)
    {
        if (string.IsNullOrWhiteSpace(logs)) return "  (no output)";
        return string.Join('\n', logs
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => $"  {l}"));
    }
}

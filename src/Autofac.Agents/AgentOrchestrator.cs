using System.Text;
using Autofac.AgentSecOps;
using Autofac.Agents.Skills;
using Autofac.Domain.AgentRuntime;
using Autofac.Integrations;
using Autofac.Sandboxes;
using Autofac.Storage.Artifacts;
using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.Options;

namespace Autofac.Agents;

/// <summary>
/// Bridges BPMN service-task execution to the agent layer.
/// When <see cref="SandboxOptions.Enabled"/> is true, dispatches to
/// <see cref="ISandboxExecutor"/> for Docker-isolated execution.
/// Outputs larger than <see cref="OutputOffloadThresholdBytes"/> are written to artifact
/// storage and replaced with a reference marker so the DB row stays small.
/// </summary>
public sealed class AgentOrchestrator : IServiceTaskExecutor
{
    private const int OutputOffloadThresholdBytes = 8192;

    private readonly ISkillRepository _skillRepository;
    private readonly IPolicyEvaluationService _policyEvaluationService;
    private readonly IGitHubConnector _gitHubConnector;
    private readonly string _gitHubBranchPrefix;
    private readonly ISandboxExecutor _sandbox;
    private readonly SandboxOptions _sandboxOptions;
    private readonly IArtifactStorage _artifactStorage;

    public AgentOrchestrator(
        ISkillRepository skillRepository,
        IPolicyEvaluationService policyEvaluationService,
        IGitHubConnector gitHubConnector,
        ISandboxExecutor sandbox,
        IArtifactStorage artifactStorage,
        IOptions<SandboxOptions> sandboxOptions,
        IOptions<IntegrationOptions> integrationOptions)
    {
        _skillRepository = skillRepository;
        _policyEvaluationService = policyEvaluationService;
        _gitHubConnector = gitHubConnector;
        _sandbox = sandbox;
        _artifactStorage = artifactStorage;
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
        var policyDecision = _policyEvaluationService.Evaluate(new PolicyEvaluationRequest(
            AgentName: metadata.Agent,
            Action: metadata.Action,
            Environment: metadata.Environment,
            PurposeType: metadata.PurposeType,
            PolicyTag: metadata.PolicyTag,
            RequiresEvidence: metadata.RequiresEvidence,
            Attempt: attempt));

        if (!string.Equals(policyDecision.Kind, "allow", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: policyDecision.Rationale,
                PolicyDecision: policyDecision,
                RuntimeSnapshot: BuildRuntimeSnapshot(runId, stepId, node.Id, metadata, profile, matchedSkillRef, skillManifest, policyDecision, allowed: false));
        }

        if (IsGitHubAction(metadata.Action))
        {
            return await RunGitHubActionAsync(runId, stepId, node, metadata, attempt, profile, matchedSkillRef, skillManifest, policyDecision, cancellationToken);
        }

        if (_sandboxOptions.Enabled)
        {
            return await RunInSandboxAsync(runId, stepId, metadata, attempt, profile, matchedSkillRef, skillManifest, policyDecision, cancellationToken);
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

        var simulatedOutput = BuildSimulatedOutput(request, profile, matchedSkillRef, skillManifest);
        var snapshot = BuildRuntimeSnapshot(runId, stepId, node.Id, metadata, profile, matchedSkillRef, skillManifest, policyDecision, allowed: true);

        var outcome = new AgentTaskOutcome(
            Succeeded: true,
            Output: simulatedOutput,
            FailureReason: null,
            PolicyDecision: policyDecision,
            RuntimeSnapshot: snapshot);

        return await MaybeOffloadOutputAsync(runId, stepId, outcome, cancellationToken);
    }

    private async Task<AgentTaskOutcome> RunGitHubActionAsync(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        AutofacTaskMetadata metadata,
        int attempt,
        AgentProfile? profile,
        AgentSkillRef? matchedSkillRef,
        SkillManifest? skillManifest,
        Domain.Persistence.PolicyDecision policyDecision,
        CancellationToken cancellationToken)
    {
        var branchName = BuildBranchName(runId, _gitHubBranchPrefix);
        var snapshot = BuildRuntimeSnapshot(runId, stepId, node.Id, metadata, profile, matchedSkillRef, skillManifest, policyDecision, allowed: true);

        try
        {
            if (string.Equals(metadata.Action, "github.create_branch", StringComparison.OrdinalIgnoreCase))
            {
                var branch = await _gitHubConnector.CreateBranchAsync(
                    new CreateGitHubBranchCommand(branchName, BaseBranch: null),
                    cancellationToken);

                var branchOutcome = new AgentTaskOutcome(
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
                    PolicyDecision: policyDecision,
                    RuntimeSnapshot: snapshot,
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
                return await MaybeOffloadOutputAsync(runId, stepId, branchOutcome, cancellationToken);
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

            var prOutcome = new AgentTaskOutcome(
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
                PolicyDecision: policyDecision,
                RuntimeSnapshot: snapshot,
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
            return await MaybeOffloadOutputAsync(runId, stepId, prOutcome, cancellationToken);
        }
        catch (Exception ex)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: ex.Message,
                PolicyDecision: policyDecision,
                RuntimeSnapshot: snapshot,
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
        AgentProfile? profile,
        AgentSkillRef? matchedSkillRef,
        SkillManifest? skillManifest,
        Domain.Persistence.PolicyDecision policyDecision,
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
        var snapshot = BuildRuntimeSnapshot(runId, stepId, "n/a", metadata, profile, matchedSkillRef, skillManifest, policyDecision, allowed: true,
            artifactNames: result.Artifacts.Keys);

        if (!result.Succeeded)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: result.Logs,
                FailureReason: result.FailureReason,
                Artifacts: result.Artifacts,
                PolicyDecision: policyDecision,
                RuntimeSnapshot: snapshot);
        }

        var output = BuildSandboxOutput(sandboxRequest, result, skillManifest);

        var sandboxOutcome = new AgentTaskOutcome(
            Succeeded: true,
            Output: output,
            FailureReason: null,
            Artifacts: result.Artifacts,
            PolicyDecision: policyDecision,
            RuntimeSnapshot: snapshot);

        return await MaybeOffloadOutputAsync(runId, stepId, sandboxOutcome, cancellationToken);
    }

    private async Task<AgentTaskOutcome> MaybeOffloadOutputAsync(
        string runId,
        string stepId,
        AgentTaskOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (outcome.Output is not { Length: > OutputOffloadThresholdBytes })
        {
            return outcome;
        }

        var artifactName = $"step_{stepId}_output.txt";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(outcome.Output));
        await _artifactStorage.SaveAsync(runId, artifactName, ms, cancellationToken);

        var mergedArtifacts = (outcome.Artifacts ?? new Dictionary<string, string>())
            .Concat([new KeyValuePair<string, string>(artifactName, artifactName)])
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return outcome with
        {
            Output = $"[artifact:{artifactName}]",
            Artifacts = mergedArtifacts
        };
    }

    private static AgentRuntimeSnapshot BuildRuntimeSnapshot(
        string runId,
        string stepId,
        string nodeId,
        AutofacTaskMetadata metadata,
        AgentProfile? profile,
        AgentSkillRef? matchedSkillRef,
        SkillManifest? skillManifest,
        Domain.Persistence.PolicyDecision policyDecision,
        bool allowed,
        IEnumerable<string>? artifactNames = null)
    {
        var contractSkills = profile?.Skills
            .Select(static s => new AgentSkillContract { SkillId = s.SkillId, Name = s.Name })
            .ToArray() ?? [];

        var contractTools = IsGitHubAction(metadata.Action)
            ? [new AgentToolContract { Name = metadata.Action, Category = AgentToolCategories.Integration }]
            : Array.Empty<AgentToolContract>();

        var skillUsage = matchedSkillRef is not null
            ?
            [
                new AgentSkillUsageRecord
                {
                    SkillId = matchedSkillRef.SkillId,
                    Name = matchedSkillRef.Name,
                    Selected = true,
                    Fingerprint = skillManifest?.Fingerprint
                }
            ]
            : Array.Empty<AgentSkillUsageRecord>();

        var toolInvocations = contractTools
            .Select(static t => new AgentToolInvocationRecord
            {
                ToolName = t.Name,
                Category = t.Category,
                Status = "completed"
            })
            .ToArray();

        var artifacts = artifactNames?
            .Select(static name => new AgentArtifactRecord { Name = name })
            .ToArray() ?? [];

        return new AgentRuntimeSnapshot
        {
            RunId = runId,
            StepId = stepId,
            NodeId = nodeId,
            AgentName = metadata.Agent,
            Action = metadata.Action,
            Contract = new AgentRuntimeContract
            {
                Prompt = new AgentPromptContract { Inline = metadata.Action },
                Skills = contractSkills,
                Tools = contractTools,
                Permissions = new AgentPermissionContract
                {
                    Level = allowed ? AgentPermissionLevels.ReadWrite : AgentPermissionLevels.ReadOnly
                },
                Outputs = AgentOutputContract.Default
            },
            Skills = skillUsage,
            ToolInvocations = toolInvocations,
            HookExecutions = [],
            Artifacts = artifacts,
            PermissionDecision = new AgentPermissionDecisionRecord
            {
                Level = allowed ? AgentPermissionLevels.ReadWrite : AgentPermissionLevels.ReadOnly,
                Allowed = allowed,
                Rationale = policyDecision.Rationale
            }
        };
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

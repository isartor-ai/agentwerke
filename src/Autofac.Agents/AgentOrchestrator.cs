using Autofac.AgentSecOps;
using Autofac.Agents.Prompts;
using Autofac.Agents.Skills;
using Autofac.Domain.AgentRuntime;
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
    private readonly IAgentPromptAssembler _promptAssembler;
    private readonly IPolicyEvaluationService _policyEvaluationService;
    private readonly IGitHubConnector _gitHubConnector;
    private readonly string _gitHubBranchPrefix;
    private readonly ISandboxExecutor _sandbox;
    private readonly SandboxOptions _sandboxOptions;

    public AgentOrchestrator(
        ISkillRepository skillRepository,
        IAgentPromptAssembler promptAssembler,
        IPolicyEvaluationService policyEvaluationService,
        IGitHubConnector gitHubConnector,
        ISandboxExecutor sandbox,
        IOptions<SandboxOptions> sandboxOptions,
        IOptions<IntegrationOptions> integrationOptions)
    {
        _skillRepository = skillRepository;
        _promptAssembler = promptAssembler;
        _policyEvaluationService = policyEvaluationService;
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
        var skillResolution = ResolveSkills(profile, matchedSkillRef, metadata.RuntimeContract);
        if (!skillResolution.Succeeded)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: skillResolution.FailureReason,
                RuntimeSnapshot: BuildRuntimeSnapshot(
                    runId,
                    stepId,
                    node,
                    metadata,
                    attempt,
                    null,
                    skillResolution.AuditSkills,
                    CreateEmptyPromptSnapshot()));
        }

        var skillManifest = skillResolution.PrimarySkill;
        var promptAssembly = _promptAssembler.Assemble(new AgentPromptAssemblyRequest(
            RunId: runId,
            StepId: stepId,
            NodeId: node.Id,
            NodeName: node.Name,
            AgentName: metadata.Agent,
            AgentDescription: profile?.Description,
            AgentCategory: profile?.Category,
            Action: metadata.Action,
            Environment: metadata.Environment,
            PurposeType: metadata.PurposeType,
            PolicyTag: metadata.PolicyTag,
            Attempt: attempt,
            RequiresEvidence: metadata.RequiresEvidence,
            Prompt: metadata.RuntimeContract?.Prompt,
            Skill: skillManifest));
        var runtimeSnapshot = BuildRuntimeSnapshot(
            runId,
            stepId,
            node,
            metadata,
            attempt,
            skillManifest,
            skillResolution.AuditSkills,
            promptAssembly.PromptSnapshot);

        if (!promptAssembly.Succeeded)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: promptAssembly.FailureReason,
                RuntimeSnapshot: runtimeSnapshot);
        }

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
                RuntimeSnapshot: runtimeSnapshot with
                {
                    PermissionDecision = new AgentPermissionDecisionRecord
                    {
                        Allowed = false,
                        Level = metadata.RuntimeContract?.Permissions.Level ?? AgentPermissionLevels.ReadOnly,
                        Rationale = policyDecision.Rationale
                    }
                });
        }

        if (IsGitHubAction(metadata.Action))
        {
            return await RunGitHubActionAsync(runId, stepId, node, metadata, attempt, policyDecision, runtimeSnapshot, cancellationToken);
        }

        if (_sandboxOptions.Enabled)
        {
            return await RunInSandboxAsync(runId, stepId, metadata, attempt, skillManifest, policyDecision, runtimeSnapshot, cancellationToken);
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
            FailureReason: null,
            PolicyDecision: policyDecision,
            RuntimeSnapshot: runtimeSnapshot with
            {
                PermissionDecision = new AgentPermissionDecisionRecord
                {
                    Allowed = true,
                    Level = metadata.RuntimeContract?.Permissions.Level ?? AgentPermissionLevels.ReadOnly,
                    Rationale = policyDecision.Rationale
                }
            });
    }

    private async Task<AgentTaskOutcome> RunGitHubActionAsync(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        AutofacTaskMetadata metadata,
        int attempt,
        Domain.Persistence.PolicyDecision policyDecision,
        AgentRuntimeSnapshot runtimeSnapshot,
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
                    PolicyDecision: policyDecision,
                    RuntimeSnapshot: runtimeSnapshot with
                    {
                        PermissionDecision = new AgentPermissionDecisionRecord
                        {
                            Allowed = true,
                            Level = metadata.RuntimeContract?.Permissions.Level ?? AgentPermissionLevels.ReadOnly,
                            Rationale = policyDecision.Rationale
                        }
                    },
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
                PolicyDecision: policyDecision,
                RuntimeSnapshot: runtimeSnapshot with
                {
                    PermissionDecision = new AgentPermissionDecisionRecord
                    {
                        Allowed = true,
                        Level = metadata.RuntimeContract?.Permissions.Level ?? AgentPermissionLevels.ReadOnly,
                        Rationale = policyDecision.Rationale
                    }
                },
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
                PolicyDecision: policyDecision,
                RuntimeSnapshot: runtimeSnapshot,
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
        Domain.Persistence.PolicyDecision policyDecision,
        AgentRuntimeSnapshot runtimeSnapshot,
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
                Artifacts: result.Artifacts,
                PolicyDecision: policyDecision,
                RuntimeSnapshot: runtimeSnapshot);
        }

        var output = BuildSandboxOutput(sandboxRequest, result, skillManifest);

        return new AgentTaskOutcome(
            Succeeded: true,
            Output: output,
            FailureReason: null,
            Artifacts: result.Artifacts,
            PolicyDecision: policyDecision,
            RuntimeSnapshot: runtimeSnapshot with
            {
                PermissionDecision = new AgentPermissionDecisionRecord
                {
                    Allowed = true,
                    Level = metadata.RuntimeContract?.Permissions.Level ?? AgentPermissionLevels.ReadOnly,
                    Rationale = policyDecision.Rationale
                }
            });
    }

    private static AgentRuntimeSnapshot BuildRuntimeSnapshot(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        AutofacTaskMetadata metadata,
        int attempt,
        SkillManifest? skillManifest,
        IReadOnlyList<AgentSkillUsageRecord> auditSkills,
        AgentPromptSnapshot promptSnapshot)
    {
        return new AgentRuntimeSnapshot
        {
            RunId = runId,
            StepId = stepId,
            NodeId = node.Id,
            AgentName = metadata.Agent,
            Action = metadata.Action,
            Prompt = promptSnapshot,
            Contract = metadata.RuntimeContract ?? new AgentRuntimeContract(),
            Skills = auditSkills,
            PermissionDecision = new AgentPermissionDecisionRecord
            {
                Allowed = true,
                Level = metadata.RuntimeContract?.Permissions.Level ?? AgentPermissionLevels.ReadOnly,
                Rationale = $"Prompt rendered for attempt {attempt}."
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
                version: {manifest.Version ?? "unspecified"}
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
                version: {manifest.Version ?? "unspecified"}
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

    private SkillResolution ResolveSkills(
        AgentProfile? profile,
        AgentSkillRef? matchedSkillRef,
        AgentRuntimeContract? runtimeContract)
    {
        var availableSkills = new Dictionary<string, ResolvedSkill>(StringComparer.OrdinalIgnoreCase);

        if (profile is not null)
        {
            foreach (var skillRef in profile.Skills)
            {
                if (string.IsNullOrWhiteSpace(skillRef.SkillManifestId))
                {
                    continue;
                }

                var manifest = _skillRepository.FindById(skillRef.SkillManifestId);
                if (manifest is null)
                {
                    return SkillResolution.Fail(
                        $"Agent profile '{profile.AgentId}' references missing skill manifest '{skillRef.SkillManifestId}' for action '{skillRef.SkillId}'.");
                }

                availableSkills[manifest.SkillId] = new ResolvedSkill(manifest, "agent-profile", Selected: false, Invoked: false);
            }
        }

        var explicitSelections = new List<ResolvedSkill>();
        foreach (var contractSkill in runtimeContract?.Skills ?? [])
        {
            var manifest = _skillRepository.FindByReference(contractSkill.SkillId)
                ?? (string.IsNullOrWhiteSpace(contractSkill.Name) ? null : _skillRepository.FindByReference(contractSkill.Name));

            if (manifest is null)
            {
                return SkillResolution.Fail(
                    $"Runtime contract references unknown skill '{contractSkill.SkillId}'. Check the skill id/name or publish the skill before running.");
            }

            if (!string.IsNullOrWhiteSpace(contractSkill.Version) &&
                !string.Equals(contractSkill.Version, manifest.Version, StringComparison.OrdinalIgnoreCase))
            {
                return SkillResolution.Fail(
                    $"Runtime contract requested skill '{contractSkill.SkillId}' version '{contractSkill.Version}', but loaded version is '{manifest.Version ?? "unspecified"}'.");
            }

            var resolved = new ResolvedSkill(manifest, "runtime-contract", Selected: contractSkill.Required, Invoked: false);
            availableSkills[manifest.SkillId] = resolved;
            explicitSelections.Add(resolved);
        }

        ResolvedSkill? primarySkill = null;
        if (explicitSelections.Count > 0)
        {
            primarySkill = explicitSelections.FirstOrDefault(static skill => skill.Selected) ?? explicitSelections[0];
        }
        else if (matchedSkillRef?.SkillManifestId is not null)
        {
            var manifest = _skillRepository.FindById(matchedSkillRef.SkillManifestId);
            if (manifest is null)
            {
                return SkillResolution.Fail(
                    $"Agent profile action '{matchedSkillRef.SkillId}' references unknown skill manifest '{matchedSkillRef.SkillManifestId}'.");
            }

            primarySkill = new ResolvedSkill(manifest, "agent-profile", Selected: true, Invoked: true);
            availableSkills[manifest.SkillId] = primarySkill;
        }

        if (primarySkill is not null)
        {
            availableSkills[primarySkill.Manifest.SkillId] = primarySkill with { Selected = true, Invoked = true };
        }

        var auditSkills = availableSkills.Values
            .OrderBy(static skill => skill.Manifest.SkillId, StringComparer.OrdinalIgnoreCase)
            .Select(static skill => new AgentSkillUsageRecord
            {
                SkillId = skill.Manifest.SkillId,
                Name = skill.Manifest.Name,
                Description = skill.Manifest.Description,
                Version = skill.Manifest.Version,
                Fingerprint = skill.Manifest.Fingerprint,
                InvocationRules = skill.Manifest.InvocationRules,
                RequiredFiles = skill.Manifest.RequiredFiles,
                OptionalTools = skill.Manifest.OptionalTools,
                Source = skill.Source,
                Available = true,
                Invoked = skill.Invoked,
                Selected = skill.Selected
            })
            .ToArray();

        return SkillResolution.Success(primarySkill?.Manifest, auditSkills);
    }

    private static AgentPromptSnapshot CreateEmptyPromptSnapshot() =>
        new(
            FinalPrompt: string.Empty,
            RenderedAt: DateTimeOffset.UtcNow.ToString("o"),
            Sections: [],
            Variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SourceFiles: []);

    private sealed record ResolvedSkill(
        SkillManifest Manifest,
        string Source,
        bool Selected,
        bool Invoked);

    private sealed record SkillResolution(
        bool Succeeded,
        SkillManifest? PrimarySkill,
        IReadOnlyList<AgentSkillUsageRecord> AuditSkills,
        string? FailureReason)
    {
        public static SkillResolution Success(SkillManifest? primarySkill, IReadOnlyList<AgentSkillUsageRecord> auditSkills) =>
            new(true, primarySkill, auditSkills, null);

        public static SkillResolution Fail(string failureReason) =>
            new(false, null, [], failureReason);
    }
}

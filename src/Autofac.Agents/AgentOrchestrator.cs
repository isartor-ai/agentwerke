using System.Text;
using Autofac.AgentSecOps;
using Autofac.Agents.Hooks;
using Autofac.Agents.Mcp;
using Autofac.Agents.Models;
using Autofac.Agents.Prompts;
using Autofac.Agents.Security;
using Autofac.Agents.Skills;
using Autofac.Agents.Tools;
using Autofac.Application.Workflows;
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
/// When sandbox execution is enabled, dispatches to <see cref="ISandboxExecutor"/>
/// for provider-selected isolated execution.
/// Outputs larger than <see cref="OutputOffloadThresholdBytes"/> are written to artifact
/// storage and replaced with a reference marker so the DB row stays small.
/// </summary>
public sealed class AgentOrchestrator : IServiceTaskExecutor
{
    private const int OutputOffloadThresholdBytes = 8192;

    private readonly ISkillRepository _skillRepository;
    private readonly IAgentPromptAssembler _promptAssembler;
    private readonly IPolicyEvaluationService _policyEvaluationService;
    private readonly IAgentHookGateway _hookGateway;
    private readonly IMcpToolSessionFactory _mcpToolSessionFactory;
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolGateway _toolGateway;
    private readonly IAgentModelRunner _modelRunner;
    private readonly ISandboxedAgentRunner _sandboxedAgentRunner;
    private readonly IRunContextRepository _runContextRepository;
    private readonly IAgentRegistry _agentRegistry;
    private readonly string _gitHubBranchPrefix;
    private readonly Autofac.Sandboxes.SandboxOptions _sandboxOptions;
    private readonly IArtifactStorage _artifactStorage;

    public AgentOrchestrator(
        ISkillRepository skillRepository,
        IAgentPromptAssembler promptAssembler,
        IPolicyEvaluationService policyEvaluationService,
        IAgentHookGateway hookGateway,
        IMcpToolSessionFactory mcpToolSessionFactory,
        IToolRegistry toolRegistry,
        IToolGateway toolGateway,
        IAgentModelRunner modelRunner,
        ISandboxedAgentRunner sandboxedAgentRunner,
        IArtifactStorage artifactStorage,
        IRunContextRepository runContextRepository,
        IAgentRegistry agentRegistry,
        IOptions<Autofac.Sandboxes.SandboxOptions> sandboxOptions,
        IOptions<IntegrationOptions> integrationOptions)
    {
        _skillRepository = skillRepository;
        _promptAssembler = promptAssembler;
        _policyEvaluationService = policyEvaluationService;
        _hookGateway = hookGateway;
        _mcpToolSessionFactory = mcpToolSessionFactory;
        _toolRegistry = toolRegistry;
        _toolGateway = toolGateway;
        _modelRunner = modelRunner;
        _sandboxedAgentRunner = sandboxedAgentRunner;
        _artifactStorage = artifactStorage;
        _runContextRepository = runContextRepository;
        _agentRegistry = agentRegistry;
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

        var profile = _agentRegistry.Find(metadata.Agent);
        var executionMode = ResolveExecutionMode(metadata, profile);
        var matchedSkillRef = ResolveSkillRef(profile, metadata.Action);
        var skillResolution = ResolveSkills(profile, matchedSkillRef, metadata.RuntimeContract);
        if (!skillResolution.Succeeded)
        {
            return await FinalizeOutcomeAsync(
                node,
                metadata,
                attempt,
                new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: skillResolution.FailureReason,
                RuntimeSnapshot: BuildRuntimeSnapshot(
                    runId,
                    stepId,
                    node,
                    metadata,
                    executionMode,
                    attempt,
                    null,
                    skillResolution.AuditSkills,
                    CreateEmptyPromptSnapshot())),
                cancellationToken);
        }

        var runContext = await LoadRunContextAsync(runId, cancellationToken);

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
            Skill: skillManifest,
            RunContext: runContext,
            SystemPrompt: profile?.SystemPrompt));
        var runtimeSnapshot = BuildRuntimeSnapshot(
            runId,
            stepId,
            node,
            metadata,
            executionMode,
            attempt,
            skillManifest,
            skillResolution.AuditSkills,
            promptAssembly.PromptSnapshot);

        if (!promptAssembly.Succeeded)
        {
            return await FinalizeOutcomeAsync(
                node,
                metadata,
                attempt,
                new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: promptAssembly.FailureReason,
                RuntimeSnapshot: runtimeSnapshot),
                cancellationToken);
        }

        var beforeRunHooks = await ExecuteHooksAsync(
            AgentHookEvents.BeforeAgentRun,
            node,
            metadata,
            attempt,
            runtimeSnapshot,
            Output: null,
            FailureReason: null,
            Artifacts: null,
            ToolName: null,
            cancellationToken);
        runtimeSnapshot = beforeRunHooks.RuntimeSnapshot;
        if (TryCreateOutcomeFromHookDecision(beforeRunHooks, metadata, runtimeSnapshot, out var hookOutcome))
        {
            return await FinalizeOutcomeAsync(node, metadata, attempt, hookOutcome, cancellationToken);
        }

        await using var mcpSession = await PrepareMcpToolsAsync(metadata, cancellationToken);
        if (mcpSession.Result is not null && !mcpSession.Result.Succeeded)
        {
            return await FinalizeOutcomeAsync(
                node,
                metadata,
                attempt,
                new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: mcpSession.Result.FailureReason,
                RuntimeSnapshot: runtimeSnapshot with
                {
                    PermissionDecision = new AgentPermissionDecisionRecord
                    {
                        Allowed = false,
                        Level = metadata.RuntimeContract?.Permissions.Level ?? AgentPermissionLevels.ReadOnly,
                        Rationale = mcpSession.Result.FailureReason
                    }
                }),
                cancellationToken);
        }

        var explicitToolRequest = BuildExplicitToolGatewayRequest(runId, stepId, node, metadata, attempt);
        if (explicitToolRequest is not null)
        {
            return await RunViaToolGatewayAsync(explicitToolRequest, node, metadata, attempt, runtimeSnapshot, cancellationToken);
        }

        if (metadata.Action.StartsWith("mcp.", StringComparison.OrdinalIgnoreCase))
        {
            return await FinalizeOutcomeAsync(
                node,
                metadata,
                attempt,
                new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: $"MCP tool '{metadata.Action}' is not registered. Check the enabled MCP servers and discovery settings.",
                RuntimeSnapshot: runtimeSnapshot),
                cancellationToken);
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
            return await FinalizeOutcomeAsync(
                node,
                metadata,
                attempt,
                new AgentTaskOutcome(
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
                }),
                cancellationToken);
        }

        var modelRunRequest = new ModelRunRequest(
            RunId: runId,
            StepId: stepId,
            AgentName: metadata.Agent,
            Action: metadata.Action,
            Environment: metadata.Environment,
            PurposeType: metadata.PurposeType,
            PolicyTag: metadata.PolicyTag,
            RequiresEvidence: metadata.RequiresEvidence,
            Attempt: attempt,
            PromptSnapshot: promptAssembly.PromptSnapshot,
            Contract: metadata.RuntimeContract ?? new AgentRuntimeContract());

        ModelRunResult modelResult;
        switch (executionMode)
        {
            case AgentExecutionModes.AgentSandboxed:
                if (!_sandboxOptions.IsEnabled)
                {
                    return await FinalizeOutcomeAsync(
                        node,
                        metadata,
                        attempt,
                        new AgentTaskOutcome(
                            Succeeded: false,
                            Output: null,
                            FailureReason: "Agent-sandboxed execution was requested, but sandbox execution is not enabled.",
                            PolicyDecision: policyDecision,
                            RuntimeSnapshot: runtimeSnapshot with
                            {
                                ExecutionMode = executionMode
                            }),
                        cancellationToken);
                }

                modelResult = await _sandboxedAgentRunner.RunAsync(
                    modelRunRequest,
                    profile,
                    metadata.SandboxProfile ?? profile?.SandboxProfiles.FirstOrDefault() ?? SandboxProfileNames.Offline,
                    cancellationToken);
                break;
            case AgentExecutionModes.ToolSandboxed:
            {
                var sandboxToolRequest = BuildSandboxExecutionToolRequest(runId, stepId, metadata, attempt, profile);
                if (sandboxToolRequest is null)
                {
                    return await FinalizeOutcomeAsync(
                        node,
                        metadata,
                        attempt,
                        new AgentTaskOutcome(
                            Succeeded: false,
                            Output: null,
                            FailureReason: "Tool-sandboxed execution was requested, but sandbox execution is not enabled.",
                            PolicyDecision: policyDecision,
                            RuntimeSnapshot: runtimeSnapshot with
                            {
                                ExecutionMode = executionMode
                            }),
                        cancellationToken);
                }

                return await RunViaToolGatewayAsync(sandboxToolRequest, node, metadata, attempt, runtimeSnapshot with
                {
                    ExecutionMode = executionMode
                }, cancellationToken);
            }
            default:
                try
                {
                    modelResult = await _modelRunner.RunAsync(modelRunRequest, cancellationToken);
                }
                catch (AgentInteractionRequiredException ex)
                {
                    // The agent asked a human mid-step and is waiting for an answer (#192).
                    // Suspend the run; the step re-runs on resume, when the answer is available.
                    return new AgentTaskOutcome(
                        Succeeded: false,
                        Output: null,
                        FailureReason: $"Awaiting response to: {ex.Prompt}",
                        PolicyDecision: policyDecision,
                        RuntimeSnapshot: runtimeSnapshot with { ExecutionMode = executionMode },
                        StepStatus: AgentTaskOutcomeStatuses.WaitingUser);
                }

                break;
        }

        var permissionDecision = new AgentPermissionDecisionRecord
        {
            Allowed = modelResult.Succeeded,
            Level = metadata.RuntimeContract?.Permissions.Level ?? AgentPermissionLevels.ReadOnly,
            Rationale = modelResult.FailureReason ?? policyDecision.Rationale
        };

        var enrichedSnapshot = runtimeSnapshot with
        {
            ToolInvocations = runtimeSnapshot.ToolInvocations.Concat(modelResult.ToolInvocations).ToArray(),
            Artifacts = runtimeSnapshot.Artifacts.Concat(MapArtifacts(modelResult.Artifacts)).ToArray(),
            PermissionDecision = permissionDecision,
            TokenUsage = modelResult.TokenUsage,
            SandboxExecution = modelResult.SandboxExecution,
            ExecutionMode = executionMode
        };

        var outcome = new AgentTaskOutcome(
            Succeeded: modelResult.Succeeded,
            Output: modelResult.Output,
            FailureReason: modelResult.FailureReason,
            PolicyDecision: policyDecision,
            RuntimeSnapshot: enrichedSnapshot,
            Artifacts: modelResult.Artifacts,
            StepStatus: modelResult.StepStatus);

        return await MaybeOffloadOutputAsync(
            runId,
            stepId,
            await FinalizeOutcomeAsync(node, metadata, attempt, outcome, cancellationToken),
            cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadRunContextAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        var entries = await _runContextRepository.GetAllAsync(runId, cancellationToken);
        if (entries.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            result[entry.Key] = entry.Value;
        }

        return result;
    }

    private async Task<AgentTaskOutcome> RunViaToolGatewayAsync(
        ToolGatewayRequest request,
        BpmnNodeDefinition node,
        AutofacTaskMetadata metadata,
        int attempt,
        AgentRuntimeSnapshot runtimeSnapshot,
        CancellationToken cancellationToken)
    {
        var beforeToolHooks = await ExecuteHooksAsync(
            AgentHookEvents.BeforeToolCall,
            node,
            metadata,
            attempt,
            runtimeSnapshot,
            Output: null,
            FailureReason: null,
            Artifacts: null,
            ToolName: request.ToolName,
            cancellationToken);
        runtimeSnapshot = beforeToolHooks.RuntimeSnapshot;
        if (TryCreateOutcomeFromHookDecision(beforeToolHooks, metadata, runtimeSnapshot, out var beforeToolOutcome))
        {
            return await FinalizeOutcomeAsync(node, metadata, attempt, beforeToolOutcome, cancellationToken);
        }

        var result = await _toolGateway.ExecuteAsync(request, cancellationToken);
        var updatedSnapshot = runtimeSnapshot with
        {
            ToolInvocations = runtimeSnapshot.ToolInvocations.Concat([result.Invocation]).ToArray(),
            Artifacts = runtimeSnapshot.Artifacts.Concat(MapArtifacts(result.Artifacts)).ToArray(),
            SandboxExecution = result.SandboxExecution ?? runtimeSnapshot.SandboxExecution,
            PermissionDecision = new AgentPermissionDecisionRecord
            {
                Allowed = result.Succeeded,
                Level = metadata.RuntimeContract?.Permissions.Level ?? AgentPermissionLevels.ReadOnly,
                Rationale = result.FailureReason ?? result.PolicyDecision?.Rationale ?? $"Tool '{request.ToolName}' completed."
            }
        };
        var toolOutcome = new AgentTaskOutcome(
            Succeeded: result.Succeeded,
            Output: result.Output,
            FailureReason: result.FailureReason,
            Artifacts: result.Artifacts,
            ExternalActions: result.ExternalActions,
            PolicyDecision: result.PolicyDecision,
            RuntimeSnapshot: updatedSnapshot);

        var afterToolHooks = await ExecuteHooksAsync(
            AgentHookEvents.AfterToolCall,
            node,
            metadata,
            attempt,
            updatedSnapshot,
            result.Output,
            result.FailureReason,
            result.Artifacts,
            request.ToolName,
            cancellationToken);
        updatedSnapshot = afterToolHooks.RuntimeSnapshot;
        toolOutcome = toolOutcome with { RuntimeSnapshot = updatedSnapshot };

        if (TryCreateOutcomeFromHookDecision(afterToolHooks, metadata, updatedSnapshot, out var afterToolOutcome))
        {
            toolOutcome = afterToolOutcome with
            {
                Artifacts = toolOutcome.Artifacts,
                ExternalActions = toolOutcome.ExternalActions,
                PolicyDecision = toolOutcome.PolicyDecision,
                RuntimeSnapshot = updatedSnapshot
            };
        }

        return await FinalizeOutcomeAsync(node, metadata, attempt, toolOutcome, cancellationToken);
    }

    private ToolGatewayRequest? BuildExplicitToolGatewayRequest(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        AutofacTaskMetadata metadata,
        int attempt)
    {
        var permissions = metadata.RuntimeContract?.Permissions ?? AgentPermissionContract.ReadOnly;
        if (string.Equals(metadata.Action, "github.create_branch", StringComparison.OrdinalIgnoreCase))
        {
            var branchName = BuildBranchName(runId, _gitHubBranchPrefix);
            return CreateToolRequest(
                ToolName: metadata.Action,
                Action: metadata.Action,
                runId,
                stepId,
                metadata,
                attempt,
                permissions,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["branch_name"] = branchName
                });
        }

        if (IsGitHubPullRequestAction(metadata.Action))
        {
            var branchName = BuildBranchName(runId, _gitHubBranchPrefix);
            return CreateToolRequest(
                ToolName: "github.create_pull_request",
                Action: metadata.Action,
                runId,
                stepId,
                metadata,
                attempt,
                permissions,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["run_id"] = runId,
                    ["step_id"] = stepId,
                    ["attempt"] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["head_branch"] = branchName,
                    ["title"] = node.Name is { Length: > 0 } ? $"Autofac: {node.Name}" : $"Autofac run {runId}",
                    ["body"] = BuildPullRequestBody(runId, stepId, metadata, attempt),
                    ["commit_message"] = $"Autofac evidence for run {runId}, step {stepId}"
                });
        }

        if (IsDirectGitHubToolAction(metadata.Action) || IsDirectCicdToolAction(metadata.Action))
        {
            return CreateToolRequest(
                ToolName: metadata.Action,
                Action: metadata.Action,
                runId,
                stepId,
                metadata,
                attempt,
                permissions,
                ExtractToolInput(metadata.RuntimeContract));
        }

        if (_toolRegistry.Find(metadata.Action) is { Category: var category } &&
            string.Equals(category, AgentToolCategories.Mcp, StringComparison.OrdinalIgnoreCase))
        {
            return CreateToolRequest(
                ToolName: metadata.Action,
                Action: metadata.Action,
                runId,
                stepId,
                metadata,
                attempt,
                permissions,
                ExtractToolInput(metadata.RuntimeContract));
        }

        return null;
    }

    private ToolGatewayRequest? BuildSandboxExecutionToolRequest(
        string runId,
        string stepId,
        AutofacTaskMetadata metadata,
        int attempt,
        AgentProfile? profile)
    {
        if (!_sandboxOptions.IsEnabled)
        {
            return null;
        }

        var permissions = metadata.RuntimeContract?.Permissions ?? AgentPermissionContract.ReadOnly;
        return CreateToolRequest(
            ToolName: "sandbox.execute",
            Action: metadata.Action,
            runId,
            stepId,
            metadata,
            attempt,
            permissions,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent_name"] = metadata.Agent,
                ["action"] = metadata.Action,
                ["environment"] = metadata.Environment ?? string.Empty,
                ["purpose_type"] = metadata.PurposeType,
                ["policy_tag"] = metadata.PolicyTag,
                ["attempt"] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["sandbox_profile"] = metadata.SandboxProfile
                    ?? profile?.SandboxProfiles.FirstOrDefault()
                    ?? SandboxProfileNames.Offline
            },
            allowedSandboxProfiles: profile?.SandboxProfiles ?? []);
    }

    private async Task<McpPreparationResult> PrepareMcpToolsAsync(
        AutofacTaskMetadata metadata,
        CancellationToken cancellationToken)
    {
        var servers = metadata.RuntimeContract?.McpServers ?? [];
        if (servers.Count == 0)
        {
            return new McpPreparationResult(null, null);
        }

        var result = await _mcpToolSessionFactory.CreateAsync(servers, cancellationToken);
        if (result.Succeeded && result.Session is not null)
        {
            _toolRegistry.RegisterRange(result.Session.Tools);
        }

        return new McpPreparationResult(result, result.Session);
    }

    private static AgentRuntimeSnapshot BuildRuntimeSnapshot(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        AutofacTaskMetadata metadata,
        string executionMode,
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
            ExecutionMode = executionMode,
            Prompt = RedactPromptSnapshot(promptSnapshot),
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

    private static AgentPromptSnapshot RedactPromptSnapshot(AgentPromptSnapshot snapshot) =>
        snapshot with
        {
            FinalPrompt = PromptRedactor.Redact(snapshot.FinalPrompt),
            Sections = snapshot.Sections
                .Select(static s => s with { Content = PromptRedactor.Redact(s.Content) })
                .ToArray()
        };

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

    private static AgentSkillRef? ResolveSkillRef(AgentProfile? profile, string action)
    {
        if (profile is null) return null;

        return profile.Skills.FirstOrDefault(s =>
            s.SupportedActions.Contains(action, StringComparer.OrdinalIgnoreCase))
            ?? profile.Skills.FirstOrDefault();
    }

    private static bool IsGitHubPullRequestAction(string action) =>
        string.Equals(action, "github.create_pull_request", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(action, "github.create_pr", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectGitHubToolAction(string action) =>
        string.Equals(action, "github.read_issue", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(action, "github.request_review", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(action, "github.post_review", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CI/CD trigger tools (#139) are deterministic, no-LLM-needed calls just like the direct
    /// GitHub tools above — a BPMN service task configured with this action dispatches straight
    /// to <see cref="Autofac.Agents.Tools.CicdTriggerDeployTool"/> instead of running an agent.
    /// </summary>
    private static bool IsDirectCicdToolAction(string action) =>
        string.Equals(action, "cicd.trigger_deploy", StringComparison.OrdinalIgnoreCase);

    private static bool IsGitHubAction(string action) =>
        string.Equals(action, "github.create_branch", StringComparison.OrdinalIgnoreCase) ||
        IsGitHubPullRequestAction(action) ||
        IsDirectGitHubToolAction(action);

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

    private async Task<HookEvaluationResult> ExecuteHooksAsync(
        string eventName,
        BpmnNodeDefinition node,
        AutofacTaskMetadata metadata,
        int attempt,
        AgentRuntimeSnapshot runtimeSnapshot,
        string? Output,
        string? FailureReason,
        IReadOnlyDictionary<string, string>? Artifacts,
        string? ToolName,
        CancellationToken cancellationToken)
    {
        var hooks = metadata.RuntimeContract?.Hooks ?? [];
        if (hooks.Count == 0)
        {
            return new HookEvaluationResult(AgentHookDecisions.Proceed, null, null, runtimeSnapshot);
        }

        var context = BuildHookContext(
            runtimeSnapshot.RunId,
            runtimeSnapshot.StepId,
            node,
            metadata,
            attempt,
            Output,
            FailureReason,
            Artifacts,
            ToolName);
        var result = await _hookGateway.ExecuteAsync(eventName, hooks, context, cancellationToken);
        var updatedSnapshot = runtimeSnapshot with
        {
            HookExecutions = runtimeSnapshot.HookExecutions.Concat(result.Records).ToArray()
        };

        return new HookEvaluationResult(result.Decision, result.OutputSummary, result.FailureReason, updatedSnapshot);
    }

    private async Task<AgentTaskOutcome> FinalizeOutcomeAsync(
        BpmnNodeDefinition node,
        AutofacTaskMetadata metadata,
        int attempt,
        AgentTaskOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (outcome.RuntimeSnapshot is null)
        {
            return outcome;
        }

        var runtimeSnapshot = outcome.RuntimeSnapshot;
        if (outcome.Artifacts is { Count: > 0 })
        {
            var artifactHooks = await ExecuteHooksAsync(
                AgentHookEvents.OnArtifactCreated,
                node,
                metadata,
                attempt,
                runtimeSnapshot,
                outcome.Output,
                outcome.FailureReason,
                outcome.Artifacts,
                ToolName: null,
                cancellationToken);
            runtimeSnapshot = artifactHooks.RuntimeSnapshot;
            outcome = outcome with { RuntimeSnapshot = runtimeSnapshot };

            if (TryCreateOutcomeFromHookDecision(artifactHooks, metadata, runtimeSnapshot, out var artifactOutcome))
            {
                outcome = MergeOutcome(outcome, artifactOutcome, runtimeSnapshot);
            }
        }

        var finalEvent = outcome.Succeeded
            ? AgentHookEvents.AfterAgentRun
            : AgentHookEvents.OnFailure;

        var finalHooks = await ExecuteHooksAsync(
            finalEvent,
            node,
            metadata,
            attempt,
            runtimeSnapshot,
            outcome.Output,
            outcome.FailureReason,
            outcome.Artifacts,
            ToolName: null,
            cancellationToken);
        runtimeSnapshot = finalHooks.RuntimeSnapshot;
        outcome = outcome with { RuntimeSnapshot = runtimeSnapshot };

        if (TryCreateOutcomeFromHookDecision(finalHooks, metadata, runtimeSnapshot, out var finalOutcome))
        {
            outcome = MergeOutcome(outcome, finalOutcome, runtimeSnapshot);
        }

        return outcome;
    }

    private static AgentTaskOutcome MergeOutcome(
        AgentTaskOutcome original,
        AgentTaskOutcome updated,
        AgentRuntimeSnapshot runtimeSnapshot) =>
        updated with
        {
            Artifacts = updated.Artifacts ?? original.Artifacts,
            ExternalActions = updated.ExternalActions ?? original.ExternalActions,
            PolicyDecision = updated.PolicyDecision ?? original.PolicyDecision,
            RuntimeSnapshot = runtimeSnapshot
        };

    private static bool TryCreateOutcomeFromHookDecision(
        HookEvaluationResult result,
        AutofacTaskMetadata metadata,
        AgentRuntimeSnapshot runtimeSnapshot,
        out AgentTaskOutcome outcome)
    {
        var rationale = result.FailureReason ?? result.OutputSummary ?? "Hook changed the agent lifecycle outcome.";
        var permissionDecision = new AgentPermissionDecisionRecord
        {
            Allowed = !string.Equals(result.Decision, AgentHookDecisions.Block, StringComparison.OrdinalIgnoreCase),
            Level = metadata.RuntimeContract?.Permissions.Level ?? AgentPermissionLevels.ReadOnly,
            Rationale = rationale
        };
        var snapshot = runtimeSnapshot with { PermissionDecision = permissionDecision };

        switch (result.Decision)
        {
            case AgentHookDecisions.Block:
                outcome = new AgentTaskOutcome(
                    Succeeded: false,
                    Output: null,
                    FailureReason: rationale,
                    RuntimeSnapshot: snapshot);
                return true;
            case AgentHookDecisions.Skip:
            case AgentHookDecisions.Override:
                outcome = new AgentTaskOutcome(
                    Succeeded: true,
                    Output: result.OutputSummary ?? rationale,
                    FailureReason: null,
                    RuntimeSnapshot: snapshot);
                return true;
            default:
                outcome = default!;
                return false;
        }
    }

    private static AgentHookContext BuildHookContext(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        AutofacTaskMetadata metadata,
        int attempt,
        string? output,
        string? failureReason,
        IReadOnlyDictionary<string, string>? artifacts,
        string? toolName)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["run_id"] = runId,
            ["step_id"] = stepId,
            ["node_id"] = node.Id,
            ["node_name"] = node.Name ?? string.Empty,
            ["agent"] = metadata.Agent,
            ["action"] = metadata.Action,
            ["environment"] = metadata.Environment ?? string.Empty,
            ["purpose_type"] = metadata.PurposeType,
            ["policy_tag"] = metadata.PolicyTag,
            ["attempt"] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["output"] = output ?? string.Empty,
            ["failure_reason"] = failureReason ?? string.Empty,
            ["tool_name"] = toolName ?? string.Empty,
            ["artifact_names"] = artifacts is null ? string.Empty : string.Join(",", artifacts.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        };

        return new AgentHookContext(
            runId,
            stepId,
            node.Id,
            node.Name ?? string.Empty,
            metadata.Agent,
            metadata.Action,
            metadata.Environment,
            metadata.PurposeType,
            metadata.PolicyTag,
            attempt,
            values);
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
                var manifest = ResolveSkillManifest(skillRef);
                if (manifest is null)
                {
                    continue;
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
        else if (matchedSkillRef is not null)
        {
            var manifest = ResolveSkillManifest(matchedSkillRef);
            if (manifest is not null)
            {
                primarySkill = new ResolvedSkill(manifest, "agent-profile", Selected: true, Invoked: true);
                availableSkills[manifest.SkillId] = primarySkill;
            }
            // else: a skill inferred from the agent PROFILE is contextual guidance, not a hard
            // requirement, so a dangling/misconfigured profile skill ref must NOT fail the step —
            // especially deterministic tool actions (e.g. github.create_branch) that use no skill.
            // Proceed without a primary skill. Only a skill explicitly REQUIRED by the runtime
            // contract fails the step (handled above). (#166)
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

    private SkillManifest? ResolveSkillManifest(AgentSkillRef skillRef)
    {
        if (!string.IsNullOrWhiteSpace(skillRef.SkillManifestId))
        {
            return _skillRepository.FindById(skillRef.SkillManifestId);
        }

        return _skillRepository.FindByReference(skillRef.SkillId)
            ?? (string.IsNullOrWhiteSpace(skillRef.Name) ? null : _skillRepository.FindByReference(skillRef.Name));
    }

    private static AgentPromptSnapshot CreateEmptyPromptSnapshot() =>
        new(
            FinalPrompt: string.Empty,
            RenderedAt: DateTimeOffset.UtcNow.ToString("o"),
            Sections: [],
            Variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SourceFiles: []);

    private sealed record HookEvaluationResult(
        string Decision,
        string? OutputSummary,
        string? FailureReason,
        AgentRuntimeSnapshot RuntimeSnapshot);

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

    private static ToolGatewayRequest CreateToolRequest(
        string ToolName,
        string Action,
        string runId,
        string stepId,
        AutofacTaskMetadata metadata,
        int attempt,
        AgentPermissionContract permissions,
        IReadOnlyDictionary<string, string> input,
        IReadOnlyList<string>? allowedSandboxProfiles = null) =>
        new(
            ToolName,
            Action,
            runId,
            stepId,
            metadata.Agent,
            metadata.Environment,
            metadata.PurposeType,
            metadata.PolicyTag,
            metadata.RequiresEvidence,
            attempt,
            permissions.Level,
            permissions.AllowedTools,
            permissions.DeniedTools,
            input,
            allowedSandboxProfiles ?? []);

    private static IReadOnlyDictionary<string, string> ExtractToolInput(AgentRuntimeContract? runtimeContract)
    {
        const string Prefix = "tool.input.";

        if (runtimeContract?.Metadata is not { Count: > 0 } metadata)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return metadata
            .Where(static pair => pair.Key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static pair => pair.Key[Prefix.Length..],
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<AgentArtifactRecord> MapArtifacts(IReadOnlyDictionary<string, string>? artifacts)
    {
        if (artifacts is null || artifacts.Count == 0)
        {
            return [];
        }

        return artifacts.Keys
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(static name => new AgentArtifactRecord
            {
                Name = name
            })
            .ToArray();
    }

    private string ResolveExecutionMode(AutofacTaskMetadata metadata, AgentProfile? profile)
    {
        if (!string.IsNullOrWhiteSpace(metadata.ExecutionMode))
        {
            return metadata.ExecutionMode;
        }

        if (string.Equals(profile?.Runner, "claude-code", StringComparison.OrdinalIgnoreCase))
        {
            return AgentExecutionModes.AgentSandboxed;
        }

        return _sandboxOptions.IsEnabled
            ? AgentExecutionModes.ToolSandboxed
            : AgentExecutionModes.Local;
    }

    private sealed record McpPreparationResult(
        McpToolSessionResult? Result,
        IMcpToolSession? Session) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (Session is not null)
            {
                await Session.DisposeAsync();
            }
        }
    }
}

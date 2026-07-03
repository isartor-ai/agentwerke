using Agentwerke.Agents.Hooks;
using Agentwerke.Agents.Models;
using Agentwerke.Agents.Prompts;
using Agentwerke.Agents.Skills;
using Agentwerke.Agents.Tools;
using Agentwerke.Agents.Mcp;
using Agentwerke.AgentSecOps;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Integrations;
using Agentwerke.Sandboxes;
using Agentwerke.Storage.Artifacts;
using Agentwerke.Workflows.Bpmn;
using Agentwerke.Workflows.Runtime;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Agentwerke.Agents.Tests;

public sealed class PolicyEvaluationServiceTests
{
    [Fact]
    public void Evaluate_WhenActionTargetsProductionDeploy_ReturnsEscalate()
    {
        var service = new PolicyEvaluationService();

        var decision = service.Evaluate(new PolicyEvaluationRequest(
            AgentName: "deploy-agent",
            Action: "cloud.deploy_artifact",
            Environment: "production",
            PurposeType: "production_deployment",
            PolicyTag: "deploy-production",
            RequiresEvidence: ["artifact_signed"],
            Attempt: 1));

        Assert.Equal("escalate", decision.Kind);
        Assert.Equal("high", decision.RiskLevel);
        Assert.Contains("Require human approval before execution.", decision.Constraints);
    }

    [Fact]
    public void Evaluate_WhenActionTouchesSecrets_ReturnsReject()
    {
        var service = new PolicyEvaluationService();

        var decision = service.Evaluate(new PolicyEvaluationRequest(
            AgentName: "security-agent",
            Action: "secret.access_export",
            Environment: "production",
            PurposeType: "secret_management",
            PolicyTag: "secret-rotation",
            RequiresEvidence: [],
            Attempt: 1));

        Assert.Equal("reject", decision.Kind);
        Assert.Equal("critical", decision.RiskLevel);
    }

    [Fact]
    public async Task CicdTriggerDeployTool_ExecuteAsync_DispatchesWorkflowWithRequestedRefAndShaInput()
    {
        var gitHub = new RecordingGitHubConnector();
        var tool = new CicdTriggerDeployTool(gitHub);

        var result = await tool.ExecuteAsync(
            new AgentToolExecutionContext(
                RunId: "run-1",
                StepId: "step-1",
                AgentName: "cicd-agent",
                Action: "cicd.trigger_deploy",
                Environment: "test",
                PurposeType: "deploy",
                PolicyTag: "deploy-test",
                Attempt: 1),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ref"] = "abc123" },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, gitHub.TriggerWorkflowDispatchCalls);
        var action = Assert.Single(result.ExternalActions!);
        Assert.Equal("trigger_workflow_dispatch", action.Action);
        Assert.Equal("dispatched", action.Status);
    }

    [Fact]
    public async Task CicdTriggerDeployTool_ExecuteAsync_WhenNoInputGiven_StillDispatchesUsingConnectorDefaults()
    {
        var gitHub = new RecordingGitHubConnector();
        var tool = new CicdTriggerDeployTool(gitHub);

        var result = await tool.ExecuteAsync(
            new AgentToolExecutionContext(
                RunId: "run-1",
                StepId: "step-1",
                AgentName: "cicd-agent",
                Action: "cicd.trigger_deploy",
                Environment: "test",
                PurposeType: "deploy",
                PolicyTag: "deploy-test",
                Attempt: 1),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, gitHub.TriggerWorkflowDispatchCalls);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenPolicyRejects_DoesNotInvokeGitHubConnector()
    {
        var skills = new SkillRepository(CreateKnownSkills());
        var policyService = new StubPolicyEvaluationService("reject");
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var assembler = new AgentPromptAssembler();
        var orchestrator = new AgentOrchestrator(
            skills,
            assembler,
            policyService,
            CreateHookGateway(),
            new StubMcpToolSessionFactory(),
            new ToolRegistry([
                new GitHubReadIssueTool(gitHub),
                new GitHubCreateBranchTool(gitHub),
                new GitHubCreatePullRequestTool(gitHub),
                new GitHubRequestReviewTool(gitHub),
                new GitHubPostReviewTool(gitHub),
                new CicdTriggerDeployTool(gitHub),
                new SandboxExecutionTool(sandbox)
            ]),
            CreateToolGateway(gitHub, sandbox, policyService),
            new NullAgentModelRunner(),
            new StubSandboxedAgentRunner(),
            new NullArtifactStorage(),
            new InMemoryRunContextRepository(),
            new FileAgentRegistry([]),
            Options.Create(new SandboxOptions()),
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    BranchPrefix = "agentwerke/run-"
                }
            }));

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "OpenPr",
                "Open Pull Request",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "github-agent",
                    Action: "github.create_pull_request",
                    Environment: null,
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [])),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("reject", outcome.PolicyDecision?.Kind);
        Assert.Equal(0, gitHub.CreateBranchCalls);
        Assert.Equal(0, gitHub.CreatePullRequestCalls);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenPromptAssemblyFails_ReturnsClearFailureReason()
    {
        var skills = new SkillRepository(CreateKnownSkills());
        var policyService = new StubPolicyEvaluationService("allow");
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var assembler = new AgentPromptAssembler();
        var orchestrator = new AgentOrchestrator(
            skills,
            assembler,
            policyService,
            CreateHookGateway(),
            new StubMcpToolSessionFactory(),
            new ToolRegistry([
                new GitHubReadIssueTool(gitHub),
                new GitHubCreateBranchTool(gitHub),
                new GitHubCreatePullRequestTool(gitHub),
                new GitHubRequestReviewTool(gitHub),
                new GitHubPostReviewTool(gitHub),
                new CicdTriggerDeployTool(gitHub),
                new SandboxExecutionTool(sandbox)
            ]),
            CreateToolGateway(gitHub, sandbox, policyService),
            new NullAgentModelRunner(),
            new StubSandboxedAgentRunner(),
            new NullArtifactStorage(),
            new InMemoryRunContextRepository(),
            new FileAgentRegistry([]),
            Options.Create(new SandboxOptions()),
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    BranchPrefix = "agentwerke/run-"
                }
            }));

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "deploy-agent",
                    Action: "cloud.deploy_artifact",
                    Environment: "staging",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Prompt = new Domain.AgentRuntime.AgentPromptContract
                        {
                            Inline = "Deploy {{missing_value}} now.",
                            StrictVariables = true
                        }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Contains("Prompt assembly failed", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenPromptVariableIsMissingInNonStrictMode_ReachesModelRunner()
    {
        var skills = new SkillRepository(CreateKnownSkills());
        var policyService = new StubPolicyEvaluationService("allow");
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var assembler = new AgentPromptAssembler();
        var orchestrator = new AgentOrchestrator(
            skills,
            assembler,
            policyService,
            CreateHookGateway(),
            new StubMcpToolSessionFactory(),
            new ToolRegistry([
                new GitHubReadIssueTool(gitHub),
                new GitHubCreateBranchTool(gitHub),
                new GitHubCreatePullRequestTool(gitHub),
                new GitHubRequestReviewTool(gitHub),
                new GitHubPostReviewTool(gitHub),
                new CicdTriggerDeployTool(gitHub),
                new SandboxExecutionTool(sandbox)
            ]),
            CreateToolGateway(gitHub, sandbox, policyService),
            new NullAgentModelRunner(),
            new StubSandboxedAgentRunner(),
            new NullArtifactStorage(),
            new InMemoryRunContextRepository(),
            new FileAgentRegistry([]),
            Options.Create(new SandboxOptions()),
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    BranchPrefix = "agentwerke/run-"
                }
            }));

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "deploy-agent",
                    Action: "cloud.deploy_artifact",
                    Environment: "staging",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Prompt = new Domain.AgentRuntime.AgentPromptContract
                        {
                            Inline = "Deploy {{output.Build}} now."
                        }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.DoesNotContain("Prompt assembly failed", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("output.Build", Assert.Single(outcome.RuntimeSnapshot!.Prompt!.MissingVariables));
        Assert.Contains("Deploy {{output.Build}} now.", outcome.RuntimeSnapshot.Prompt.FinalPrompt);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenModelIsNotConfigured_ReturnsNeedsConfigStepStatus()
    {
        var skills = new SkillRepository(CreateKnownSkills());
        var policyService = new StubPolicyEvaluationService("allow");
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var assembler = new AgentPromptAssembler();
        var orchestrator = new AgentOrchestrator(
            skills,
            assembler,
            policyService,
            CreateHookGateway(),
            new StubMcpToolSessionFactory(),
            new ToolRegistry([
                new GitHubReadIssueTool(gitHub),
                new GitHubCreateBranchTool(gitHub),
                new GitHubCreatePullRequestTool(gitHub),
                new GitHubRequestReviewTool(gitHub),
                new GitHubPostReviewTool(gitHub),
                new CicdTriggerDeployTool(gitHub),
                new SandboxExecutionTool(sandbox)
            ]),
            CreateToolGateway(gitHub, sandbox, policyService),
            new NullAgentModelRunner(),
            new StubSandboxedAgentRunner(),
            new NullArtifactStorage(),
            new InMemoryRunContextRepository(),
            new FileAgentRegistry([]),
            Options.Create(new SandboxOptions()),
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    BranchPrefix = "agentwerke/run-"
                }
            }));

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "WriteSpec",
                "Write Spec",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "spec-agent",
                    Action: "spec.generate",
                    Environment: "dev",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [])),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(AgentTaskOutcomeStatuses.NeedsConfig, outcome.StepStatus);
        Assert.Contains("Anthropic:ApiKey", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenRuntimeContractSelectsSkill_TracksAvailableAndInvokedSkills()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", gitHub, sandbox, sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "deploy-agent",
                    Action: "deploy",
                    Environment: "staging",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract
                        {
                            Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite
                        },
                        Skills =
                        [
                            new Domain.AgentRuntime.AgentSkillContract
                            {
                                SkillId = "security-and-hardening",
                                Required = true
                            }
                        ]
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.RuntimeSnapshot);
        var snapshot = outcome.RuntimeSnapshot!;
        Assert.Equal("security-and-hardening", Assert.Single(snapshot.Skills, static s => s.Invoked).SkillId);
        Assert.Contains(snapshot.Skills, static s => s.SkillId == "shipping-and-launch" && s.Available && !s.Invoked);
        Assert.Contains(snapshot.Skills, static s => s.SkillId == "security-and-hardening" && s.Selected && s.Invoked && s.Source == "runtime-contract");
        Assert.Single(snapshot.ToolInvocations);
        Assert.Equal("sandbox.execute", snapshot.ToolInvocations[0].ToolName);
        Assert.Equal("allow", snapshot.ToolInvocations[0].PolicyDecisionKind);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenRuntimeContractReferencesUnknownSkill_FailsEarly()
    {
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", new RecordingGitHubConnector(), new StubSandboxExecutor());

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "deploy-agent",
                    Action: "deploy",
                    Environment: "staging",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Skills =
                        [
                            new Domain.AgentRuntime.AgentSkillContract
                            {
                                SkillId = "does-not-exist"
                            }
                        ]
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Contains("unknown skill", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenAgentProfileReferencesUnknownSkill_DoesNotFailStep()
    {
        // #166: a skill referenced by the agent PROFILE (not the runtime contract) that can't be
        // resolved must not fail the step — unlike an explicit runtime-contract skill (above).
        // Deterministic tool actions like github.create_branch use no skill at all.
        var gitHub = new RecordingGitHubConnector();
        var registry = new FileAgentRegistry(
        [
            new AgentProfile
            {
                AgentId = "github-agent",
                Name = "GitHub Agent",
                Runner = "agent-model",
                SupportedActions = ["github.create_branch"],
                Skills =
                [
                    new AgentSkillRef("github-branching", "Branching", "Create branches",
                        ["github.create_branch"], SkillManifestId: "does-not-exist")
                ]
            }
        ]);

        var orchestrator = CreateOrchestrator(
            CreateKnownSkills(), "allow", gitHub, new StubSandboxExecutor(), registry: registry);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "CreateBranch",
                "Create Branch",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "github-agent",
                    Action: "github.create_branch",
                    Environment: "github",
                    PurposeType: "repo-write",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [])),
            attempt: 1,
            CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.FailureReason);
        // The unresolved profile skill was skipped (nothing invoked) and the deterministic
        // GitHub tool still ran.
        Assert.DoesNotContain(outcome.RuntimeSnapshot!.Skills, static s => s.Invoked);
        Assert.Contains(outcome.RuntimeSnapshot!.ToolInvocations, static t => t.ToolName == "github.create_branch");
    }

    [Fact]
    public async Task AgentOrchestrator_WhenRuntimeContractRequestsMismatchedSkillVersion_FailsEarly()
    {
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", new RecordingGitHubConnector(), new StubSandboxExecutor());

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "deploy-agent",
                    Action: "deploy",
                    Environment: "staging",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Skills =
                        [
                            new Domain.AgentRuntime.AgentSkillContract
                            {
                                SkillId = "shipping-and-launch",
                                Version = "9.9.9"
                            }
                        ]
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Contains("loaded version", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenToolIsDeniedByPermissions_BlocksExecutionBeforeConnectorCall()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", gitHub, sandbox, sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "OpenPr",
                "Open Pull Request",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "github-agent",
                    Action: "github.create_pull_request",
                    Environment: null,
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract
                        {
                            Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite,
                            DeniedTools = ["github.create_pull_request"]
                        }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, gitHub.CreatePullRequestCalls);
        Assert.NotNull(outcome.RuntimeSnapshot);
        Assert.Equal("blocked", Assert.Single(outcome.RuntimeSnapshot!.ToolInvocations).Status);
    }

    [Theory]
    [InlineData("github.read_issue")]
    [InlineData("github.request_review")]
    [InlineData("github.post_review")]
    [InlineData("cicd.trigger_deploy")]
    public async Task AgentOrchestrator_WhenNewGitHubToolIsDenied_BlocksExecutionBeforeConnectorCall(string action)
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", gitHub, sandbox, sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "GitHubCollaboration",
                "GitHub Collaboration",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "github-agent",
                    Action: action,
                    Environment: "github",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract
                        {
                            Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite,
                            DeniedTools = [action]
                        },
                        Metadata = BuildGitHubToolMetadata(action)
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, gitHub.GetIssueCalls);
        Assert.Equal(0, gitHub.RequestReviewersCalls);
        Assert.Equal(0, gitHub.PostReviewCalls);
        Assert.Equal(0, gitHub.TriggerWorkflowDispatchCalls);
        Assert.NotNull(outcome.RuntimeSnapshot);
        var invocation = Assert.Single(outcome.RuntimeSnapshot!.ToolInvocations);
        Assert.Equal(action, invocation.ToolName);
        Assert.Equal("blocked", invocation.Status);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenSandboxRunsUnderReadOnlyPermissions_BlocksShellTool()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", gitHub, sandbox, sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "deploy-agent",
                    Action: "deploy",
                    Environment: "staging",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = Domain.AgentRuntime.AgentPermissionContract.ReadOnly
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, sandbox.ExecuteCalls);
        Assert.NotNull(outcome.RuntimeSnapshot);
        var invocation = Assert.Single(outcome.RuntimeSnapshot!.ToolInvocations);
        Assert.Equal("sandbox.execute", invocation.ToolName);
        Assert.Equal("blocked", invocation.Status);
    }

    [Fact]
    public async Task AgentOrchestrator_DeployAgent_DefaultsToDeclaredDeploymentProfile()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", gitHub, sandbox, sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "deploy-agent",
                    Action: "deploy",
                    Environment: "staging",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract { Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, sandbox.ExecuteCalls);
        Assert.Equal("deployment", sandbox.LastRequest?.Metadata?["agentwerke.sandboxProfile"]);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenWorkflowRequestsProfileAgentIsNotAllowedToUse_RejectsBeforeSandboxCreate()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", gitHub, sandbox, sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Review",
                "Review",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "security-agent",
                    Action: "scan",
                    Environment: null,
                    PurposeType: "security-scan",
                    PolicyTag: "security-scan",
                    RequiresEvidence: [],
                    SandboxProfile: "deployment",
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract { Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, sandbox.ExecuteCalls);
        Assert.Contains("not authorized", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
        var invocation = Assert.Single(outcome.RuntimeSnapshot!.ToolInvocations);
        Assert.Equal("profile_rejected", invocation.Status);
    }

    [Fact]
    public async Task AgentOrchestrator_GenericAgentWithNoDeclaredProfiles_DefaultsToOffline()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", gitHub, sandbox, sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Analyze",
                "Analyze",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "not-a-registered-agent",
                    Action: "analyze",
                    Environment: null,
                    PurposeType: "analysis",
                    PolicyTag: "doc-generation",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract { Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, sandbox.ExecuteCalls);
        Assert.Equal("offline", sandbox.LastRequest?.Metadata?["agentwerke.sandboxProfile"]);
    }

    [Fact]
    public async Task AgentOrchestrator_ClaudeCodeAgent_UsesSandboxedAgentRunner()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var sandboxedRunner = new StubSandboxedAgentRunner();
        var claudeCodeRegistry = new FileAgentRegistry(
        [
            new AgentProfile
            {
                AgentId = "spec-writer",
                Name = "Spec Writer",
                Runner = "claude-code",
                SupportedActions = ["spec.generate"],
                SandboxProfiles = ["offline"]
            }
        ]);

        var orchestrator = CreateOrchestrator(
            CreateKnownSkills(),
            "allow",
            gitHub,
            sandbox,
            sandboxEnabled: true,
            registry: claudeCodeRegistry,
            sandboxedAgentRunner: sandboxedRunner);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "WriteSpec",
                "Write Spec",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "spec-writer",
                    Action: "spec.generate",
                    Environment: "ci",
                    PurposeType: "specification",
                    PolicyTag: "doc-generation",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Prompt = new Domain.AgentRuntime.AgentPromptContract
                        {
                            Inline = "Write a concise spec."
                        }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, sandbox.ExecuteCalls);
        Assert.Equal(1, sandboxedRunner.ExecuteCalls);
        Assert.Equal(AgentExecutionModes.AgentSandboxed, outcome.RuntimeSnapshot!.ExecutionMode);
        Assert.NotNull(outcome.RuntimeSnapshot.SandboxExecution);
    }

    [Fact]
    public async Task AgentOrchestrator_ClaudeCodeAgent_WhenSandboxDisabled_ReturnsClearFailure()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var sandboxedRunner = new StubSandboxedAgentRunner();
        var claudeCodeRegistry = new FileAgentRegistry(
        [
            new AgentProfile
            {
                AgentId = "spec-writer",
                Name = "Spec Writer",
                Runner = "claude-code",
                SupportedActions = ["spec.generate"],
                SandboxProfiles = ["offline"]
            }
        ]);

        var orchestrator = CreateOrchestrator(
            CreateKnownSkills(),
            "allow",
            gitHub,
            sandbox,
            sandboxEnabled: false,
            registry: claudeCodeRegistry,
            sandboxedAgentRunner: sandboxedRunner);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "WriteSpec",
                "Write Spec",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "spec-writer",
                    Action: "spec.generate",
                    Environment: "ci",
                    PurposeType: "specification",
                    PolicyTag: "doc-generation",
                    RequiresEvidence: [])),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, sandbox.ExecuteCalls);
        Assert.Equal(0, sandboxedRunner.ExecuteCalls);
        Assert.Equal(AgentExecutionModes.AgentSandboxed, outcome.RuntimeSnapshot!.ExecutionMode);
        Assert.Contains("sandbox execution is not enabled", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenAgentRequestsUnknownProfileName_RejectsBeforeSandboxCreate()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", gitHub, sandbox, sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "deploy-agent",
                    Action: "deploy",
                    Environment: "staging",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    SandboxProfile: "super-admin",
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract { Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, sandbox.ExecuteCalls);
        Assert.Contains("Unknown sandbox profile", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentOrchestrator_GitHubAgent_RequestsDeploymentProfile_RejectedAsNotInAllowList()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", gitHub, sandbox, sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Sync",
                "Sync",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "github-agent",
                    Action: "github.sync_status",
                    Environment: null,
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    SandboxProfile: "deployment",
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract { Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, sandbox.ExecuteCalls);
        Assert.Contains("not authorized", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenGitHubToolExecutes_RecordsInvocationAndExternalActions()
    {
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(CreateKnownSkills(), "allow", gitHub, sandbox);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "OpenPr",
                "Open Pull Request",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "github-agent",
                    Action: "github.create_pull_request",
                    Environment: null,
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract
                        {
                            Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite,
                            AllowedTools = ["github.create_pull_request"]
                        }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, gitHub.CreateBranchCalls);
        Assert.Equal(1, gitHub.CreatePullRequestCalls);
        Assert.NotNull(outcome.RuntimeSnapshot);
        var invocation = Assert.Single(outcome.RuntimeSnapshot!.ToolInvocations);
        Assert.Equal("github.create_pull_request", invocation.ToolName);
        Assert.Equal("completed", invocation.Status);
        Assert.Equal("allow", invocation.PolicyDecisionKind);
        Assert.NotNull(outcome.ExternalActions);
        Assert.Equal(2, outcome.ExternalActions!.Count);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenMcpToolExecutes_RoutesThroughGatewayAndPersistsInvocation()
    {
        var mcpSessionFactory = new StubMcpToolSessionFactory(
            session: new StubMcpToolSession([
                new StubMcpAgentTool("mcp.weather.lookup", requiredInputs: ["location"])
            ]));
        var orchestrator = CreateOrchestrator(
            CreateKnownSkills(),
            "allow",
            new RecordingGitHubConnector(),
            new StubSandboxExecutor(),
            sandboxEnabled: false,
            mcpSessionFactory: mcpSessionFactory);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "WeatherLookup",
                "Weather Lookup",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "ops-agent",
                    Action: "mcp.weather.lookup",
                    Environment: "staging",
                    PurposeType: "investigation",
                    PolicyTag: "read-only",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract
                        {
                            Level = Domain.AgentRuntime.AgentPermissionLevels.ReadOnly,
                            AllowedTools = ["mcp.weather.lookup"]
                        },
                        McpServers =
                        [
                            new Domain.AgentRuntime.AgentMcpServerContract
                            {
                                Name = "weather"
                            }
                        ],
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["tool.input.location"] = "Berlin"
                        }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, mcpSessionFactory.CreateCalls);
        Assert.NotNull(outcome.RuntimeSnapshot);
        var invocation = Assert.Single(outcome.RuntimeSnapshot!.ToolInvocations);
        Assert.Equal("mcp.weather.lookup", invocation.ToolName);
        Assert.Equal(Domain.AgentRuntime.AgentToolCategories.Mcp, invocation.Category);
        Assert.Equal("completed", invocation.Status);
        Assert.Contains("Berlin", invocation.InputSummary);
        Assert.Equal("weather:Berlin", outcome.Output);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenMcpToolIsDenied_BlocksExecutionBeforeToolRuns()
    {
        var tool = new StubMcpAgentTool("mcp.weather.lookup");
        var orchestrator = CreateOrchestrator(
            CreateKnownSkills(),
            "allow",
            new RecordingGitHubConnector(),
            new StubSandboxExecutor(),
            sandboxEnabled: false,
            mcpSessionFactory: new StubMcpToolSessionFactory(new StubMcpToolSession([tool])));

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "WeatherLookup",
                "Weather Lookup",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "ops-agent",
                    Action: "mcp.weather.lookup",
                    Environment: "staging",
                    PurposeType: "investigation",
                    PolicyTag: "read-only",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract
                        {
                            Level = Domain.AgentRuntime.AgentPermissionLevels.ReadOnly,
                            DeniedTools = ["mcp.weather.lookup"]
                        },
                        McpServers =
                        [
                            new Domain.AgentRuntime.AgentMcpServerContract
                            {
                                Name = "weather"
                            }
                        ]
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, tool.ExecuteCalls);
        Assert.NotNull(outcome.RuntimeSnapshot);
        Assert.Equal("blocked", Assert.Single(outcome.RuntimeSnapshot!.ToolInvocations).Status);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenMcpStartupFails_ReturnsClearFailureReason()
    {
        var orchestrator = CreateOrchestrator(
            CreateKnownSkills(),
            "allow",
            new RecordingGitHubConnector(),
            new StubSandboxExecutor(),
            sandboxEnabled: false,
            mcpSessionFactory: new StubMcpToolSessionFactory(
                failureReason: "MCP startup failed: connection refused"));

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "WeatherLookup",
                "Weather Lookup",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "ops-agent",
                    Action: "mcp.weather.lookup",
                    Environment: "staging",
                    PurposeType: "investigation",
                    PolicyTag: "read-only",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        McpServers =
                        [
                            new Domain.AgentRuntime.AgentMcpServerContract
                            {
                                Name = "weather"
                            }
                        ]
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Contains("MCP startup failed", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(outcome.RuntimeSnapshot);
        Assert.False(outcome.RuntimeSnapshot!.PermissionDecision?.Allowed);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenBeforeAgentRunHookBlocks_FailsBeforeExecution()
    {
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(
            CreateKnownSkills(),
            "allow",
            new RecordingGitHubConnector(),
            sandbox,
            sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "deploy-agent",
                    Action: "deploy",
                    Environment: "staging",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract
                        {
                            Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite
                        },
                        Hooks =
                        [
                            new Domain.AgentRuntime.AgentHookContract
                            {
                                Name = "policy-guard",
                                Event = AgentHookEvents.BeforeAgentRun,
                                Type = "internal-policy",
                                Blocking = true,
                                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["decision"] = AgentHookDecisions.Block,
                                    ["reason"] = "Blocked before execution."
                                }
                            }
                        ]
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("Blocked before execution.", outcome.FailureReason);
        Assert.Equal(0, sandbox.ExecuteCalls);
        var hook = Assert.Single(outcome.RuntimeSnapshot!.HookExecutions);
        Assert.Equal("policy-guard", hook.HookName);
        Assert.Equal(AgentHookDecisions.Block, hook.Decision);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenBeforeToolCallHookBlocks_PreventsToolSideEffects()
    {
        var gitHub = new RecordingGitHubConnector();
        var orchestrator = CreateOrchestrator(
            CreateKnownSkills(),
            "allow",
            gitHub,
            new StubSandboxExecutor());

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "OpenPr",
                "Open Pull Request",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "github-agent",
                    Action: "github.create_pull_request",
                    Environment: null,
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract
                        {
                            Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite,
                            AllowedTools = ["github.create_pull_request"]
                        },
                        Hooks =
                        [
                            new Domain.AgentRuntime.AgentHookContract
                            {
                                Name = "tool-guard",
                                Event = AgentHookEvents.BeforeToolCall,
                                Type = "template",
                                Blocking = true,
                                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["decision"] = AgentHookDecisions.Block,
                                    ["reason"] = "Blocked {{tool_name}}."
                                }
                            }
                        ]
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, gitHub.CreateBranchCalls);
        Assert.Equal(0, gitHub.CreatePullRequestCalls);
        Assert.Equal("Blocked github.create_pull_request.", outcome.FailureReason);
        Assert.Single(outcome.RuntimeSnapshot!.HookExecutions);
        Assert.Contains(outcome.RuntimeSnapshot!.HookExecutions, static hook => hook.Event == AgentHookEvents.BeforeToolCall);
    }

    [Fact]
    public async Task AgentOrchestrator_WhenAfterToolCallAndArtifactHooksRun_PersistsTheirDecisions()
    {
        var mcpSessionFactory = new StubMcpToolSessionFactory(
            session: new StubMcpToolSession([
                new StubMcpAgentTool("mcp.weather.lookup", requiredInputs: ["location"])
            ]));
        var orchestrator = CreateOrchestrator(
            CreateKnownSkills(),
            "allow",
            new RecordingGitHubConnector(),
            new StubSandboxExecutor(),
            sandboxEnabled: false,
            mcpSessionFactory: mcpSessionFactory);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "WeatherLookup",
                "Weather Lookup",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "ops-agent",
                    Action: "mcp.weather.lookup",
                    Environment: "staging",
                    PurposeType: "investigation",
                    PolicyTag: "read-only",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract
                        {
                            Level = Domain.AgentRuntime.AgentPermissionLevels.ReadOnly,
                            AllowedTools = ["mcp.weather.lookup"]
                        },
                        McpServers =
                        [
                            new Domain.AgentRuntime.AgentMcpServerContract
                            {
                                Name = "weather"
                            }
                        ],
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["tool.input.location"] = "Berlin"
                        },
                        Hooks =
                        [
                            new Domain.AgentRuntime.AgentHookContract
                            {
                                Name = "after-tool",
                                Event = AgentHookEvents.AfterToolCall,
                                Type = "template",
                                Blocking = false,
                                FailureMode = Domain.AgentRuntime.AgentHookFailureModes.FailOpen,
                                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["decision"] = AgentHookDecisions.Proceed,
                                    ["output"] = "tool={{tool_name}}"
                                }
                            },
                            new Domain.AgentRuntime.AgentHookContract
                            {
                                Name = "artifact-audit",
                                Event = AgentHookEvents.OnArtifactCreated,
                                Type = "template",
                                Blocking = false,
                                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["decision"] = AgentHookDecisions.Proceed,
                                    ["output"] = "artifacts={{artifact_names}}"
                                }
                            }
                        ]
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, mcpSessionFactory.CreateCalls);
        Assert.Contains(outcome.RuntimeSnapshot!.HookExecutions, static hook => hook.HookName == "after-tool" && hook.OutputSummary == "tool=mcp.weather.lookup");
        Assert.Contains(outcome.RuntimeSnapshot!.HookExecutions, static hook => hook.HookName == "artifact-audit" && hook.OutputSummary == "artifacts=mcp-result.json");
    }

    [Fact]
    public async Task AgentOrchestrator_WhenHookTypeIsUnsupportedButFailOpen_ContinuesAndRecordsFailure()
    {
        var sandbox = new StubSandboxExecutor();
        var orchestrator = CreateOrchestrator(
            CreateKnownSkills(),
            "allow",
            new RecordingGitHubConnector(),
            sandbox,
            sandboxEnabled: true);

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AgentwerkeTaskMetadata(
                    Agent: "deploy-agent",
                    Action: "deploy",
                    Environment: "staging",
                    PurposeType: "implementation",
                    PolicyTag: "repo-change",
                    RequiresEvidence: [],
                    RuntimeContract: new Domain.AgentRuntime.AgentRuntimeContract
                    {
                        Permissions = new Domain.AgentRuntime.AgentPermissionContract
                        {
                            Level = Domain.AgentRuntime.AgentPermissionLevels.ReadWrite
                        },
                        Hooks =
                        [
                            new Domain.AgentRuntime.AgentHookContract
                            {
                                Name = "future-hook",
                                Event = AgentHookEvents.BeforeAgentRun,
                                Type = "http",
                                Blocking = false,
                                FailureMode = Domain.AgentRuntime.AgentHookFailureModes.FailOpen
                            }
                        ]
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, sandbox.ExecuteCalls);
        var hook = Assert.Single(outcome.RuntimeSnapshot!.HookExecutions);
        Assert.Equal(AgentHookDecisions.FailOpen, hook.Decision);
        Assert.Contains("unsupported type", hook.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static AgentOrchestrator CreateOrchestrator(
        IReadOnlyList<SkillManifest> manifests,
        string policyKind,
        RecordingGitHubConnector gitHub,
        StubSandboxExecutor sandbox,
        bool sandboxEnabled = false,
        IMcpToolSessionFactory? mcpSessionFactory = null,
        IAgentRegistry? registry = null,
        ISandboxedAgentRunner? sandboxedAgentRunner = null)
    {
        var policyService = new StubPolicyEvaluationService(policyKind);
        var toolRegistry = new ToolRegistry(
        [
            new GitHubReadIssueTool(gitHub),
            new GitHubCreateBranchTool(gitHub),
            new GitHubCreatePullRequestTool(gitHub),
            new GitHubRequestReviewTool(gitHub),
            new GitHubPostReviewTool(gitHub),
                new CicdTriggerDeployTool(gitHub),
            new SandboxExecutionTool(sandbox)
        ]);

        return new AgentOrchestrator(
            new SkillRepository(manifests),
            new AgentPromptAssembler(),
            policyService,
            CreateHookGateway(),
            mcpSessionFactory ?? new StubMcpToolSessionFactory(),
            toolRegistry,
            new ToolGateway(toolRegistry, policyService, new SandboxProfileSelector()),
            new NullAgentModelRunner(),
            sandboxedAgentRunner ?? new StubSandboxedAgentRunner(),
            new NullArtifactStorage(),
            new InMemoryRunContextRepository(),
            registry ?? new FileAgentRegistry([]),
            Options.Create(new SandboxOptions
            {
                Enabled = sandboxEnabled
            }),
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    BranchPrefix = "agentwerke/run-"
                }
            }));
    }

    private static IAgentHookGateway CreateHookGateway() =>
        new HookGateway(
        [
            new InternalPolicyHookHandler(),
            new TemplateHookHandler()
        ]);

    private static IToolGateway CreateToolGateway(
        RecordingGitHubConnector gitHub,
        StubSandboxExecutor sandbox,
        IPolicyEvaluationService policyService)
    {
        var registry = new ToolRegistry(
        [
            new GitHubReadIssueTool(gitHub),
            new GitHubCreateBranchTool(gitHub),
            new GitHubCreatePullRequestTool(gitHub),
            new GitHubRequestReviewTool(gitHub),
            new GitHubPostReviewTool(gitHub),
                new CicdTriggerDeployTool(gitHub),
            new SandboxExecutionTool(sandbox)
        ]);

        return new ToolGateway(registry, policyService, new SandboxProfileSelector());
    }

    private static SkillManifest[] CreateKnownSkills() =>
    [
        new(
            SkillId: "shipping-and-launch",
            Name: "Shipping and Launch",
            Description: "Ship changes safely.",
            Version: "1.0.0",
            InvocationRules: ["deploy", "release"],
            RequiredFiles: [],
            OptionalTools: ["git"],
            Content: "Always validate the deploy.",
            Fingerprint: new string('a', 64),
            FilePath: "/skills/shipping-and-launch/SKILL.md"),
        new(
            SkillId: "security-and-hardening",
            Name: "Security and Hardening",
            Description: "Protect sensitive operations.",
            Version: "2.1.0",
            InvocationRules: ["security", "credentials"],
            RequiredFiles: [],
            OptionalTools: ["rg"],
            Content: "Review security-sensitive changes carefully.",
            Fingerprint: new string('b', 64),
            FilePath: "/skills/security-and-hardening/SKILL.md"),
        new(
            SkillId: "test-driven-development",
            Name: "Test Driven Development",
            Description: "Write tests first.",
            Version: "1.4.0",
            InvocationRules: ["tests"],
            RequiredFiles: [],
            OptionalTools: ["dotnet test"],
            Content: "Start with a failing test.",
            Fingerprint: new string('c', 64),
            FilePath: "/skills/test-driven-development/SKILL.md"),
        new(
            SkillId: "git-workflow-and-versioning",
            Name: "Git Workflow and Versioning",
            Description: "Keep branches clean and traceable.",
            Version: "1.1.0",
            InvocationRules: ["branching", "pr"],
            RequiredFiles: [],
            OptionalTools: ["git"],
            Content: "Use intentional commits.",
            Fingerprint: new string('d', 64),
            FilePath: "/skills/git-workflow-and-versioning/SKILL.md"),
        new(
            SkillId: "incremental-implementation",
            Name: "Incremental Implementation",
            Description: "Ship work in small slices.",
            Version: "1.3.0",
            InvocationRules: ["incremental"],
            RequiredFiles: [],
            OptionalTools: ["rg"],
            Content: "Land changes step by step.",
            Fingerprint: new string('e', 64),
            FilePath: "/skills/incremental-implementation/SKILL.md")
    ];

    private static IReadOnlyDictionary<string, string> BuildGitHubToolMetadata(string action) =>
        action switch
        {
            "github.read_issue" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool.input.issue_number"] = "135"
            },
            "github.request_review" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool.input.pull_number"] = "42",
                ["tool.input.reviewers"] = "alice,bob"
            },
            "github.post_review" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool.input.pull_number"] = "42",
                ["tool.input.body"] = "Looks good.",
                ["tool.input.event"] = "COMMENT"
            },
            "cicd.trigger_deploy" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool.input.ref"] = "abc123"
            },
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

    private sealed class NullArtifactStorage : Agentwerke.Storage.Artifacts.IArtifactStorage
    {
        public Task<IReadOnlyList<Agentwerke.Storage.Artifacts.ArtifactDescriptor>> ListAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Agentwerke.Storage.Artifacts.ArtifactDescriptor>>([]);
        public Task SaveAsync(string runId, string artifactName, Stream content, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string runId, string artifactName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(Stream.Null);
        public Task<bool> ExistsAsync(string runId, string artifactName, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class StubPolicyEvaluationService : IPolicyEvaluationService
    {
        private readonly string _kind;

        public StubPolicyEvaluationService(string kind)
        {
            _kind = kind;
        }

        public Domain.Persistence.PolicyDecision Evaluate(PolicyEvaluationRequest request)
        {
            return new Domain.Persistence.PolicyDecision
            {
                Kind = _kind,
                PolicyId = "test-policy",
                PolicyName = "Test Policy",
                Rationale = "Blocked by test policy.",
                RiskScore = 90,
                RiskLevel = _kind == "allow" ? "low" : "high",
                DecidedAt = DateTime.UtcNow.ToString("o")
            };
        }
    }

    private sealed class RecordingGitHubConnector : IGitHubConnector
    {
        public int GetIssueCalls { get; private set; }
        public int CreateBranchCalls { get; private set; }
        public int CreatePullRequestCalls { get; private set; }
        public int RequestReviewersCalls { get; private set; }
        public int PostReviewCalls { get; private set; }

        public Task<GitHubIssueResult> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default)
        {
            GetIssueCalls++;
            return Task.FromResult(new GitHubIssueResult(
                issueNumber,
                "Issue title",
                "Issue body",
                ["enhancement"],
                "open",
                $"https://example.test/issues/{issueNumber}",
                []));
        }

        public Task<GitHubBranchResult> CreateBranchAsync(CreateGitHubBranchCommand command, CancellationToken cancellationToken = default)
        {
            CreateBranchCalls++;
            return Task.FromResult(new GitHubBranchResult(command.BranchName, "main", "sha", "https://example.test/branch", false));
        }

        public Task<GitHubPullRequestResult> CreatePullRequestAsync(CreateGitHubPullRequestCommand command, CancellationToken cancellationToken = default)
        {
            CreatePullRequestCalls++;
            return Task.FromResult(new GitHubPullRequestResult(42, "https://example.test/pr/42", command.HeadBranch, "main", "sha", ".agentwerke/test.md", false));
        }

        public Task<GitHubPullRequestStatusResult> GetPullRequestAsync(int pullNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubCheckStatusResult> GetCheckStatusAsync(string @ref, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubReviewRequestResult> RequestReviewersAsync(RequestGitHubReviewersCommand command, CancellationToken cancellationToken = default)
        {
            RequestReviewersCalls++;
            return Task.FromResult(new GitHubReviewRequestResult(command.PullNumber, $"https://example.test/pr/{command.PullNumber}", command.Reviewers));
        }

        public Task<GitHubReviewResult> PostReviewAsync(PostGitHubReviewCommand command, CancellationToken cancellationToken = default)
        {
            PostReviewCalls++;
            return Task.FromResult(new GitHubReviewResult(7, command.PullNumber, $"https://example.test/pr/{command.PullNumber}#review-7", "COMMENTED", command.Event));
        }

        public int TriggerWorkflowDispatchCalls { get; private set; }

        public Task<GitHubWorkflowDispatchResult> TriggerWorkflowDispatchAsync(TriggerGitHubWorkflowDispatchCommand command, CancellationToken cancellationToken = default)
        {
            TriggerWorkflowDispatchCalls++;
            return Task.FromResult(new GitHubWorkflowDispatchResult(
                command.WorkflowFileName ?? "deploy-to-test.yml",
                command.Ref ?? "main",
                DateTimeOffset.UtcNow.ToString("o")));
        }
    }

    private sealed class StubSandboxExecutor : ISandboxExecutor
    {
        public int ExecuteCalls { get; private set; }

        public SandboxExecutionRequest? LastRequest { get; private set; }

        public Task<SandboxExecutionResult> ExecuteAsync(SandboxExecutionRequest request, CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            LastRequest = request;
            return Task.FromResult(new SandboxExecutionResult(
                Succeeded: true,
                ExitCode: 0,
                Logs: "ok",
                FailureReason: null,
                Duration: TimeSpan.FromSeconds(1),
                Artifacts: new Dictionary<string, string>()));
        }
    }

    private sealed class StubSandboxedAgentRunner : ISandboxedAgentRunner
    {
        public int ExecuteCalls { get; private set; }

        public Task<ModelRunResult> RunAsync(
            ModelRunRequest request,
            AgentProfile? profile,
            string sandboxProfileName,
            CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            return Task.FromResult(new ModelRunResult(
                Succeeded: true,
                Output: "sandboxed-output",
                FailureReason: null,
                ToolInvocations: [],
                Artifacts: null,
                TokenUsage: new AgentModelTokenUsage(10, 20, "claude-sonnet-4-6"),
                SandboxExecution: new AgentSandboxExecutionRecord
                {
                    Provider = "opensandbox",
                    SandboxId = "sbx-123",
                    CommandState = "Completed",
                    ExitCode = 0,
                    DurationMs = 100
                }));
        }
    }

    private sealed class StubMcpToolSessionFactory : IMcpToolSessionFactory
    {
        private readonly IMcpToolSession? _session;
        private readonly string? _failureReason;

        public StubMcpToolSessionFactory(IMcpToolSession? session = null, string? failureReason = null)
        {
            _session = session;
            _failureReason = failureReason;
        }

        public int CreateCalls { get; private set; }

        public Task<McpToolSessionResult> CreateAsync(
            IReadOnlyList<Domain.AgentRuntime.AgentMcpServerContract> servers,
            CancellationToken cancellationToken)
        {
            CreateCalls++;

            if (_failureReason is not null)
            {
                return Task.FromResult(new McpToolSessionResult(false, null, _failureReason));
            }

            return Task.FromResult(new McpToolSessionResult(true, _session ?? new StubMcpToolSession([]), null));
        }
    }

    private sealed class StubMcpToolSession : IMcpToolSession
    {
        public StubMcpToolSession(IReadOnlyList<IAgentTool> tools)
        {
            Tools = tools;
        }

        public IReadOnlyList<IAgentTool> Tools { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubMcpAgentTool : IAgentTool
    {
        private readonly string[] _requiredInputs;

        public StubMcpAgentTool(string name, IReadOnlyList<string>? requiredInputs = null)
        {
            Name = name;
            _requiredInputs = requiredInputs?.ToArray() ?? [];
        }

        public int ExecuteCalls { get; private set; }

        public string Name { get; }

        public string Category => Domain.AgentRuntime.AgentToolCategories.Mcp;

        public void Validate(IReadOnlyDictionary<string, string> input)
        {
            foreach (var requiredInput in _requiredInputs)
            {
                if (!input.ContainsKey(requiredInput))
                {
                    throw new InvalidOperationException($"Missing '{requiredInput}'.");
                }
            }
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(
            AgentToolExecutionContext context,
            IReadOnlyDictionary<string, string> input,
            CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            var location = input.TryGetValue("location", out var value) ? value : "unknown";
            return Task.FromResult(new AgentToolExecutionResult(
                true,
                $"weather:{location}",
                null,
                new Dictionary<string, string>
                {
                    ["mcp-result.json"] = JsonSerializer.Serialize(input)
                }));
        }
    }
}

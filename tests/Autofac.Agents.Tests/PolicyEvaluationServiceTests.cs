using Autofac.Agents.Hooks;
using Autofac.Agents.Models;
using Autofac.Agents.Prompts;
using Autofac.Agents.Skills;
using Autofac.Agents.Tools;
using Autofac.Agents.Mcp;
using Autofac.AgentSecOps;
using Autofac.Integrations;
using Autofac.Sandboxes;
using Autofac.Storage.Artifacts;
using Autofac.Workflows.Bpmn;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Autofac.Agents.Tests;

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
                new GitHubCreateBranchTool(gitHub),
                new GitHubCreatePullRequestTool(gitHub),
                new SandboxExecutionTool(sandbox)
            ]),
            CreateToolGateway(gitHub, sandbox, policyService),
            new NullAgentModelRunner(),
            new NullArtifactStorage(),
            Options.Create(new SandboxOptions()),
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    BranchPrefix = "autofac/run-"
                }
            }));

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "OpenPr",
                "Open Pull Request",
                "serviceTask",
                new AutofacTaskMetadata(
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
                new GitHubCreateBranchTool(gitHub),
                new GitHubCreatePullRequestTool(gitHub),
                new SandboxExecutionTool(sandbox)
            ]),
            CreateToolGateway(gitHub, sandbox, policyService),
            new NullAgentModelRunner(),
            new NullArtifactStorage(),
            Options.Create(new SandboxOptions()),
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    BranchPrefix = "autofac/run-"
                }
            }));

        var outcome = await orchestrator.ExecuteAsync(
            "run-123",
            "step-456",
            new BpmnNodeDefinition(
                "Deploy",
                "Deploy",
                "serviceTask",
                new AutofacTaskMetadata(
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
                            Inline = "Deploy {{missing_value}} now."
                        }
                    })),
            attempt: 1,
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Contains("Prompt assembly failed", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
                new AutofacTaskMetadata(
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
        IMcpToolSessionFactory? mcpSessionFactory = null)
    {
        var policyService = new StubPolicyEvaluationService(policyKind);
        var registry = new ToolRegistry(
        [
            new GitHubCreateBranchTool(gitHub),
            new GitHubCreatePullRequestTool(gitHub),
            new SandboxExecutionTool(sandbox)
        ]);

        return new AgentOrchestrator(
            new SkillRepository(manifests),
            new AgentPromptAssembler(),
            policyService,
            CreateHookGateway(),
            mcpSessionFactory ?? new StubMcpToolSessionFactory(),
            registry,
            new ToolGateway(registry, policyService),
            new NullAgentModelRunner(),
            new NullArtifactStorage(),
            Options.Create(new SandboxOptions
            {
                Enabled = sandboxEnabled
            }),
            Options.Create(new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    BranchPrefix = "autofac/run-"
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
            new GitHubCreateBranchTool(gitHub),
            new GitHubCreatePullRequestTool(gitHub),
            new SandboxExecutionTool(sandbox)
        ]);

        return new ToolGateway(registry, policyService);
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

    private sealed class NullArtifactStorage : Autofac.Storage.Artifacts.IArtifactStorage
    {
        public Task<IReadOnlyList<Autofac.Storage.Artifacts.ArtifactDescriptor>> ListAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Autofac.Storage.Artifacts.ArtifactDescriptor>>([]);
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
        public int CreateBranchCalls { get; private set; }
        public int CreatePullRequestCalls { get; private set; }

        public Task<GitHubBranchResult> CreateBranchAsync(CreateGitHubBranchCommand command, CancellationToken cancellationToken = default)
        {
            CreateBranchCalls++;
            return Task.FromResult(new GitHubBranchResult(command.BranchName, "main", "sha", "https://example.test/branch", false));
        }

        public Task<GitHubPullRequestResult> CreatePullRequestAsync(CreateGitHubPullRequestCommand command, CancellationToken cancellationToken = default)
        {
            CreatePullRequestCalls++;
            return Task.FromResult(new GitHubPullRequestResult(42, "https://example.test/pr/42", command.HeadBranch, "main", "sha", ".autofac/test.md", false));
        }
    }

    private sealed class StubSandboxExecutor : ISandboxExecutor
    {
        public int ExecuteCalls { get; private set; }

        public Task<SandboxExecutionResult> ExecuteAsync(SandboxExecutionRequest request, CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            return Task.FromResult(new SandboxExecutionResult(
                Succeeded: true,
                ExitCode: 0,
                Logs: "ok",
                FailureReason: null,
                Duration: TimeSpan.FromSeconds(1),
                Artifacts: new Dictionary<string, string>()));
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

using Autofac.Agents.Prompts;
using Autofac.Agents.Skills;
using Autofac.AgentSecOps;
using Autofac.Integrations;
using Autofac.Sandboxes;
using Autofac.Workflows.Bpmn;
using Microsoft.Extensions.Options;

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
        var skills = new SkillRepository(Array.Empty<SkillManifest>());
        var policyService = new StubPolicyEvaluationService("reject");
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var assembler = new AgentPromptAssembler();
        var orchestrator = new AgentOrchestrator(
            skills,
            assembler,
            policyService,
            gitHub,
            sandbox,
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
        var skills = new SkillRepository(Array.Empty<SkillManifest>());
        var policyService = new StubPolicyEvaluationService("allow");
        var gitHub = new RecordingGitHubConnector();
        var sandbox = new StubSandboxExecutor();
        var assembler = new AgentPromptAssembler();
        var orchestrator = new AgentOrchestrator(
            skills,
            assembler,
            policyService,
            gitHub,
            sandbox,
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
        public Task<SandboxExecutionResult> ExecuteAsync(SandboxExecutionRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SandboxExecutionResult(
                Succeeded: true,
                ExitCode: 0,
                Logs: "ok",
                FailureReason: null,
                Duration: TimeSpan.FromSeconds(1),
                Artifacts: new Dictionary<string, string>()));
        }
    }
}

using Agentwerke.Agents.Models;
using Agentwerke.Agents.Tools;
using Agentwerke.AgentSecOps;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;
using Agentwerke.Sandboxes;
using Agentwerke.Workflows.Runtime;

namespace Agentwerke.Agents.Tests;

/// <summary>
/// Human-in-the-loop escalation when an agent needs a tool that exists but is not allowed for
/// its step (#202): the run pauses on a blocking tool_access interaction; "approve" allows the
/// tool for the rest of the run, any other reply is fed back to the model as operator guidance.
/// </summary>
public sealed class ToolAccessEscalationTests
{
    private const string RunId = "run-esc";

    [Fact]
    public async Task Gateway_DeniedTool_PersistsPendingInteractionAndSuspends()
    {
        var interactions = new InMemoryInteractionRepository();
        var gateway = CreateGateway(interactions, out _);

        var ex = await Assert.ThrowsAsync<AgentInteractionRequiredException>(() =>
            gateway.ExecuteAsync(Request(allowedTools: ["github.read_issue"]), CancellationToken.None));

        Assert.Contains("github.post_review", ex.Prompt, StringComparison.Ordinal);
        var interaction = Assert.Single(interactions.Items);
        Assert.Equal(AgentInteractionKinds.ToolAccess, interaction.Kind);
        Assert.Equal(AgentInteractionStatuses.Pending, interaction.Status);
        Assert.True(interaction.Blocking);
        Assert.Equal(["approve", "deny", "fail"], interaction.Options);
        // Operator context (#202): tool name and the model's stated intent are recorded.
        Assert.Equal("github.post_review", interaction.ToolName);
        Assert.Contains("pull_number", interaction.Intent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gateway_DeniedToolStillPending_DoesNotDuplicateTheInteraction()
    {
        var interactions = new InMemoryInteractionRepository();
        var gateway = CreateGateway(interactions, out _);

        await Assert.ThrowsAsync<AgentInteractionRequiredException>(() =>
            gateway.ExecuteAsync(Request(allowedTools: ["github.read_issue"]), CancellationToken.None));
        await Assert.ThrowsAsync<AgentInteractionRequiredException>(() =>
            gateway.ExecuteAsync(Request(allowedTools: ["github.read_issue"]), CancellationToken.None));

        Assert.Single(interactions.Items);
    }

    [Fact]
    public async Task Gateway_ApprovedInteraction_ExecutesTheTool()
    {
        var interactions = new InMemoryInteractionRepository();
        var gateway = CreateGateway(interactions, out var tool);
        interactions.Items.Add(AnsweredInteraction("approve"));

        var result = await gateway.ExecuteAsync(Request(allowedTools: ["github.read_issue"]), CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.NotNull(tool.LastInput);
    }

    [Fact]
    public async Task Gateway_DeclinedInteraction_ReturnsOperatorGuidanceAsToolResult()
    {
        var interactions = new InMemoryInteractionRepository();
        var gateway = CreateGateway(interactions, out var tool);
        interactions.Items.Add(AnsweredInteraction("Summarize the review as an issue comment instead."));

        var result = await gateway.ExecuteAsync(Request(allowedTools: ["github.read_issue"]), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Summarize the review as an issue comment instead.", result.FailureReason, StringComparison.Ordinal);
        Assert.Equal("blocked", result.Invocation.Status);
        Assert.Null(tool.LastInput);
    }

    [Fact]
    public async Task Gateway_FailAnswer_ThrowsStepFailure()
    {
        var interactions = new InMemoryInteractionRepository();
        var gateway = CreateGateway(interactions, out var tool);
        interactions.Items.Add(AnsweredInteraction("fail", respondedBy: "ops@example.com"));

        var ex = await Assert.ThrowsAsync<ToolAccessStepFailedException>(() =>
            gateway.ExecuteAsync(Request(allowedTools: ["github.read_issue"]), CancellationToken.None));

        Assert.Equal("github.post_review", ex.ToolName);
        Assert.Contains("ops@example.com", ex.Message, StringComparison.Ordinal);
        Assert.Null(tool.LastInput);
    }

    [Fact]
    public async Task Gateway_FailFastMode_FailsWithoutEscalating()
    {
        var interactions = new InMemoryInteractionRepository();
        var gateway = CreateGateway(interactions, out var tool);

        var result = await gateway.ExecuteAsync(
            Request(allowedTools: ["github.read_issue"]) with { ToolEscalation = "fail" },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("blocked", result.Invocation.Status);
        Assert.Contains("not included in the runtime contract allowlist", result.FailureReason, StringComparison.Ordinal);
        Assert.Empty(interactions.Items);
        Assert.Null(tool.LastInput);
    }

    [Fact]
    public async Task SandboxExecutor_EscalatableTool_PausesWithWaitingUserResult()
    {
        var executor = CreateExecutor(planToolName: "github.post_review");

        var result = await executor.ExecuteAsync(
            Envelope(escalatableTools: ["github.post_review"]),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AgentTaskOutcomeStatuses.WaitingUser, result.StepStatus);
        Assert.Contains("github.post_review", result.PendingToolAccessPrompt, StringComparison.Ordinal);
        Assert.Contains("Awaiting response to:", result.FailureReason, StringComparison.Ordinal);
        // Operator context (#202) survives the sandbox boundary via the result payload.
        Assert.Equal("github.post_review", result.PendingToolAccessToolName);
        Assert.Contains("pull_number", result.PendingToolAccessIntent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SandboxExecutor_DeclinedTool_ReturnsGuidanceToTheModel()
    {
        var executor = CreateExecutor(planToolName: "github.post_review");

        var result = await executor.ExecuteAsync(
            Envelope(toolAccessGuidance: new Dictionary<string, string>
            {
                ["github.post_review"] = "Comment on the issue instead.",
            }),
            CancellationToken.None);

        // The model got the guidance as a tool result and finished its turn normally.
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Null(result.StepStatus);
        var invocation = Assert.Single(result.ToolInvocations!);
        Assert.Equal("blocked", invocation.Status);
        Assert.Contains("Comment on the issue instead.", invocation.ErrorMessage, StringComparison.Ordinal);
    }

    private static ToolGateway CreateGateway(InMemoryInteractionRepository interactions, out StubTool tool)
    {
        tool = new StubTool("github.post_review", AgentToolCategories.Integration);
        return new ToolGateway(
            new ToolRegistry([tool]),
            new AllowAllPolicyService(),
            new SandboxProfileSelector(),
            interactions);
    }

    private static SandboxedAgentRuntimeExecutor CreateExecutor(string planToolName) =>
        new(
            new PlannedModelClient(planToolName),
            new NullMcpToolSessionFactory(),
            new ToolRegistry([]));

    private static ToolGatewayRequest Request(IReadOnlyList<string> allowedTools) =>
        new(
            ToolName: "github.post_review",
            Action: "review.pr",
            RunId: RunId,
            StepId: "step-1",
            AgentName: "senior-reviewer",
            Environment: "ci",
            PurposeType: "review",
            PolicyTag: "demo-review",
            RequiresEvidence: [],
            Attempt: 1,
            PermissionLevel: AgentPermissionLevels.ReadWrite,
            AllowedTools: allowedTools,
            DeniedTools: [],
            Input: new Dictionary<string, string> { ["pull_number"] = "23" });

    private static SandboxedAgentRunEnvelope Envelope(
        IReadOnlyList<string>? escalatableTools = null,
        IReadOnlyDictionary<string, string>? toolAccessGuidance = null) =>
        new(
            RunId: RunId,
            StepId: "step-1",
            AgentName: "senior-reviewer",
            Action: "review.pr",
            Environment: "ci",
            PurposeType: "review",
            PolicyTag: "demo-review",
            Attempt: 1,
            SystemPrompt: "You review pull requests.",
            UserPrompt: "Review PR 23.",
            Model: "llama-3.3-70b-instruct",
            MaxTokens: 1024,
            Contract: new AgentRuntimeContract(),
            ResolvedTools: [],
            SubAgents: [],
            RemainingSubAgentDepth: 0,
            EscalatableTools: escalatableTools,
            ToolAccessGuidance: toolAccessGuidance);

    private static AgentInteraction AnsweredInteraction(string response, string? respondedBy = null) =>
        new()
        {
            Id = "int-1",
            RunId = RunId,
            FromAgent = "senior-reviewer",
            Kind = AgentInteractionKinds.ToolAccess,
            AddresseeType = AgentInteractionAddresseeTypes.Human,
            Blocking = true,
            Prompt = ToolAccessEscalation.BuildPrompt("senior-reviewer", "github.post_review"),
            Status = AgentInteractionStatuses.Answered,
            Response = response,
            RespondedBy = respondedBy,
            CreatedAt = DateTime.UtcNow.ToString("o"),
        };

    private sealed class StubTool(string name, string category) : IAgentTool
    {
        public IReadOnlyDictionary<string, string>? LastInput { get; private set; }

        public string Name => name;

        public string Category => category;

        public void Validate(IReadOnlyDictionary<string, string> input)
        {
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(
            AgentToolExecutionContext context,
            IReadOnlyDictionary<string, string> input,
            CancellationToken cancellationToken)
        {
            LastInput = input;
            return Task.FromResult(new AgentToolExecutionResult(true, "Review posted.", null));
        }
    }

    private sealed class AllowAllPolicyService : IPolicyEvaluationService
    {
        public PolicyDecision Evaluate(PolicyEvaluationRequest request) =>
            new()
            {
                Kind = "allow",
                PolicyId = "test-policy",
                PolicyName = "Test Policy",
                Rationale = "Allowed by test policy.",
                RiskScore = 5,
                RiskLevel = "low",
            };
    }

    /// <summary>Calls the planned tool once, then answers "done" — enough to drive the executor.</summary>
    private sealed class PlannedModelClient(string toolName) : ILanguageModelClient
    {
        public async Task<LanguageModelResponse> RunAsync(
            LanguageModelRequest request,
            Func<LanguageModelToolCall, CancellationToken, Task<LanguageModelToolResult>> toolExecutor,
            CancellationToken cancellationToken,
            AgentExecutionProgressReporter? progressReporter = null)
        {
            await toolExecutor(
                new LanguageModelToolCall("call-1", toolName, new Dictionary<string, string> { ["pull_number"] = "23" }),
                cancellationToken);

            return new LanguageModelResponse(
                Succeeded: true,
                Output: "done",
                FailureReason: null,
                AllToolCalls: [],
                Usage: new LanguageModelTokenUsage(1, 1),
                ModelId: "stub");
        }
    }

    private sealed class NullMcpToolSessionFactory : Agentwerke.Agents.Mcp.IMcpToolSessionFactory
    {
        public Task<Agentwerke.Agents.Mcp.McpToolSessionResult> CreateAsync(
            IReadOnlyList<AgentMcpServerContract>? servers,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Agentwerke.Agents.Mcp.McpToolSessionResult(true, null, null));
    }
}

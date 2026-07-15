using Agentwerke.Agents;
using Agentwerke.Agents.Knowledge;
using Agentwerke.Agents.Models;
using Agentwerke.Agents.Tools;
using Agentwerke.AgentSecOps;
using Agentwerke.Application.Agents;
using Agentwerke.Domain.Persistence;
using Agentwerke.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agentwerke.Agents.Tests;

/// <summary>
/// Guards the DI wiring that the API host depends on at startup — in particular the
/// IHttpClientFactory-based registration of the Anthropic client added for issue #143. A broken
/// registration here would surface as "API did not become healthy" in the Docker E2E suite.
/// </summary>
public sealed class AgentsDependencyInjectionTests
{
    private static IServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddAgentwerkeAgents(config);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }

    [Fact]
    public void WithApiKey_ResolvesRealClientThroughHttpClientFactory()
    {
        using var provider = (ServiceProvider)Build(("Anthropic:ApiKey", "test-key"));
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ILanguageModelClient>();
        var retryHandler = scope.ServiceProvider.GetRequiredService<AnthropicRetryHandler>();

        Assert.IsType<AnthropicLanguageModelClient>(client);
        Assert.NotNull(retryHandler);
    }

    [Fact]
    public void WithoutApiKey_ResolvesNullClient()
    {
        using var provider = (ServiceProvider)Build();
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ILanguageModelClient>();

        Assert.IsType<NullLanguageModelClient>(client);
    }

    [Fact]
    public void ProviderMock_ResolvesMockClient_EvenWithoutApiKey()
    {
        using var provider = (ServiceProvider)Build(("Anthropic:Provider", "mock"));
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ILanguageModelClient>();

        Assert.IsType<MockLanguageModelClient>(client);
    }

    [Fact]
    public void ProviderAnthropicWithoutKey_StillSelectsAnthropic()
    {
        // Explicit provider=anthropic resolves the real client even without a key
        // (it surfaces a clear "not configured" failure at call time, not a silent mock).
        using var provider = (ServiceProvider)Build(("Anthropic:Provider", "anthropic"));
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ILanguageModelClient>();

        Assert.IsType<AnthropicLanguageModelClient>(client);
    }

    [Fact]
    public void ToolGraph_ResolvesWithoutCircularDependency()
    {
        // Regression for the DI cycle that failed the API host's container validation at startup
        // ("API did not become healthy"): AgentRequestTool -> IAgentModelRunner -> IToolGateway ->
        // IToolRegistry -> IEnumerable<IAgentTool> -> AgentRequestTool. Resolving IToolGateway forces
        // construction of the whole tool set (incl. AgentRequestTool), which throws pre-fix. The fix
        // injects the model runner as Lazy<> so the edge is deferred past construction (#192/#196).
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentwerkeAgents(config);
        // Deps the tool set needs that other modules register in production.
        services.AddScoped<IGitHubConnector, StubGitHubConnector>();
        services.AddScoped<IPolicyEvaluationService, StubPolicyEvaluationService>();
        services.AddScoped<ISandboxProfileSelector, StubSandboxProfileSelector>();
        services.AddScoped<IAgentInteractionRepository, StubInteractionRepository>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var gateway = scope.ServiceProvider.GetRequiredService<IToolGateway>();
        var registry = scope.ServiceProvider.GetRequiredService<IToolRegistry>();

        Assert.NotNull(gateway);
        Assert.NotNull(registry);
    }

    [Fact]
    public void KnowledgeRetriever_ResolvesConfiguredCorpus()
    {
        using var provider = (ServiceProvider)Build(
            ("Knowledge:Documents:0:Source", "quickstart.md"),
            ("Knowledge:Documents:0:Text", "Quickstart runs with Docker Compose."));

        var retriever = provider.GetRequiredService<IKnowledgeRetriever>();
        var results = retriever.Search("docker quickstart", topK: 1);

        var snippet = Assert.Single(results);
        Assert.Equal("quickstart.md", snippet.Source);
    }

    // Construction-only stubs for the container-resolution regression test above: their methods are
    // never invoked (the test only resolves/constructs the graph), so they throw.
    private sealed class StubGitHubConnector : IGitHubConnector
    {
        public Task<GitHubIssueResult> GetIssueAsync(int issueNumber, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GitHubIssueCommentPostResult> CommentIssueAsync(CommentGitHubIssueCommand command, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GitHubIssueStateResult> CloseIssueAsync(CloseGitHubIssueCommand command, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GitHubBranchResult> CreateBranchAsync(CreateGitHubBranchCommand command, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GitHubPullRequestResult> CreatePullRequestAsync(CreateGitHubPullRequestCommand command, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GitHubPullRequestStatusResult> GetPullRequestAsync(int pullNumber, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GitHubCheckStatusResult> GetCheckStatusAsync(string @ref, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GitHubReviewRequestResult> RequestReviewersAsync(RequestGitHubReviewersCommand command, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GitHubReviewResult> PostReviewAsync(PostGitHubReviewCommand command, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GitHubWorkflowDispatchResult> TriggerWorkflowDispatchAsync(TriggerGitHubWorkflowDispatchCommand command, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class StubPolicyEvaluationService : IPolicyEvaluationService
    {
        public PolicyDecision Evaluate(PolicyEvaluationRequest request) => throw new NotImplementedException();
    }

    private sealed class StubSandboxProfileSelector : ISandboxProfileSelector
    {
        public SandboxProfileSelectionResult Select(SandboxProfileSelectionRequest request) => throw new NotImplementedException();
    }

    private sealed class StubInteractionRepository : IAgentInteractionRepository
    {
        public Task AddAsync(AgentInteraction interaction, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AgentInteraction>> GetByRunAsync(string runId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AgentInteraction>>([]);
        public Task<IReadOnlyList<AgentInteraction>> GetPostsForRunAsync(string runId, string? fromFilter, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AgentInteraction>>([]);
        public Task<AgentInteraction?> GetByIdAsync(string interactionId, CancellationToken cancellationToken) => Task.FromResult<AgentInteraction?>(null);
        public Task<AgentInteraction?> GetPendingForRunAsync(string runId, CancellationToken cancellationToken) => Task.FromResult<AgentInteraction?>(null);
        public Task<InteractionTransitionResult> TryTransitionAsync(string interactionId, string toStatus, string? response, string? respondedBy, string? respondedChannel, CancellationToken cancellationToken) => Task.FromResult(new InteractionTransitionResult(InteractionTransitionOutcome.NotFound, null));
        public Task<IReadOnlyList<AgentInteraction>> GetPendingAsync(string? runId, string? addresseeType, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AgentInteraction>>([]);
        public Task<IReadOnlyList<AgentInteraction>> GetDueForExpiryAsync(string nowIso, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AgentInteraction>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

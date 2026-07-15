using Agentwerke.Agents.Hooks;
using Agentwerke.Agents.Mcp;
using Agentwerke.Agents.Models;
using Agentwerke.Agents.Prompts;
using Agentwerke.Agents.Skills;
using Agentwerke.Agents.Tools;
using Agentwerke.Sandboxes;
using Agentwerke.Workflows.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentwerkeAgents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var paths = AgentRegistryPaths.Resolve(configuration);
        services.AddSingleton(paths);
        services.AddSingleton<IAgentRegistry>(new FileAgentRegistry(paths));
        services.AddSingleton<ISkillRepository>(new SkillRepository(paths.SkillsDirectory));
        services.AddSingleton<IAgentRegistryEditor, FileAgentRegistryEditor>();
        services.AddSingleton<IAgentPromptAssembler, AgentPromptAssembler>();
        services.AddScoped<IAgentTool, GitHubReadIssueTool>();
        services.AddScoped<IAgentTool, GitHubCommentIssueTool>();
        services.AddScoped<IAgentTool, GitHubCloseIssueTool>();
        services.AddScoped<IAgentTool, GitHubCreateBranchTool>();
        services.AddScoped<IAgentTool, GitHubCreatePullRequestTool>();
        services.AddScoped<IAgentTool, GitHubRequestReviewTool>();
        services.AddScoped<IAgentTool, GitHubPostReviewTool>();
        services.AddScoped<IAgentTool, CicdTriggerDeployTool>();
        services.AddScoped<IAgentTool, SandboxExecutionTool>();
        services.Configure<Knowledge.KnowledgeOptions>(configuration.GetSection(Knowledge.KnowledgeOptions.Section));
        services.AddSingleton<Knowledge.IKnowledgeRetriever>(sp =>
            new Knowledge.LexicalKnowledgeRetriever(
                sp.GetRequiredService<IOptions<Knowledge.KnowledgeOptions>>()));
        services.AddScoped<IAgentTool, KnowledgeSearchTool>();
        services.AddScoped<Coordination.IAgentCoordinationChannel, Coordination.PersistentAgentCoordinationChannel>();
        services.AddScoped<IAgentTool, AgentPostMessageTool>();
        services.AddScoped<IAgentTool, AgentReadMessagesTool>();
        services.AddScoped<IAgentTool, HumanAskTool>();
        services.AddScoped<IAgentTool, HumanConfirmTool>();
        services.AddScoped<IAgentTool, HumanNotifyTool>();
        services.AddScoped<IAgentTool, AgentRequestTool>();
        services.AddScoped<IAgentHookHandler, InternalPolicyHookHandler>();
        services.AddScoped<IAgentHookHandler, TemplateHookHandler>();
        services.AddScoped<IAgentHookGateway, HookGateway>();
        services.AddScoped<IMcpClientFactory, McpClientFactory>();
        services.AddScoped<IMcpToolSessionFactory, McpToolSessionFactory>();
        services.AddScoped<IToolRegistry, ToolRegistry>();
        services.AddScoped<IToolGateway, ToolGateway>();
        services.AddAgentwerkeSandboxes(configuration);

        // Language model client selection (see LanguageModelOptions.Provider):
        //   mock              → deterministic, zero-cost client for demos/CI (#151)
        //   anthropic         → real Anthropic client (default when an API key is present)
        //   openai | litellm  → any OpenAI Chat Completions-compatible endpoint (#174)
        //   else              → null client (agent steps report "no model configured")
        services.Configure<LanguageModelOptions>(configuration.GetSection(LanguageModelOptions.Section));
        var apiKey = configuration[$"{LanguageModelOptions.Section}:ApiKey"];
        var provider = (configuration[$"{LanguageModelOptions.Section}:Provider"] ?? string.Empty)
            .Trim().ToLowerInvariant();
        var useAnthropic = provider == "anthropic"
            || (provider is "" or "auto" && !string.IsNullOrWhiteSpace(apiKey));

        if (provider == "mock")
        {
            services.AddScoped<ILanguageModelClient, MockLanguageModelClient>();
        }
        else if (provider is "openai" or "litellm")
        {
            // Any OpenAI-compatible endpoint (OpenAI, Azure OpenAI, or a LiteLLM proxy).
            // Same pooled/timeout-bounded HttpClient + transient-retry pipeline as Anthropic.
            services.AddTransient<AnthropicRetryHandler>();
            services.AddHttpClient<OpenAiCompatibleLanguageModelClient>((sp, client) =>
                {
                    var options = sp.GetRequiredService<IOptions<LanguageModelOptions>>().Value;
                    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
                })
                .AddHttpMessageHandler<AnthropicRetryHandler>();
            services.AddScoped<ILanguageModelClient>(sp =>
                sp.GetRequiredService<OpenAiCompatibleLanguageModelClient>());
        }
        else if (useAnthropic)
        {
            // Resolve the Anthropic client through IHttpClientFactory so it gets a pooled,
            // timeout-bounded HttpClient with transient-failure retries (429/529/5xx) — matching
            // how the GitHub/Jira/Camunda connectors are wired.
            services.AddTransient<AnthropicRetryHandler>();
            services.AddHttpClient<AnthropicLanguageModelClient>((sp, client) =>
                {
                    var options = sp.GetRequiredService<IOptions<LanguageModelOptions>>().Value;
                    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
                })
                .AddHttpMessageHandler<AnthropicRetryHandler>();
            services.AddScoped<ILanguageModelClient>(sp =>
                sp.GetRequiredService<AnthropicLanguageModelClient>());
        }
        else
        {
            services.AddScoped<ILanguageModelClient, NullLanguageModelClient>();
        }
        services.AddSingleton<IModelRunBudget, ModelRunBudget>();
        services.AddScoped<IAgentModelRunner, AgentModelRunner>();
        // Lazy indirection so AgentRequestTool can depend on the model runner without forming a
        // construction-time DI cycle (the runner transitively depends on the tool set).
        services.AddScoped(sp => new Lazy<IAgentModelRunner>(sp.GetRequiredService<IAgentModelRunner>));
        services.AddScoped<ISandboxedAgentRunner, OpenSandboxedAgentRunner>();

        services.AddScoped<IServiceTaskExecutor, AgentOrchestrator>();

        return services;
    }
}

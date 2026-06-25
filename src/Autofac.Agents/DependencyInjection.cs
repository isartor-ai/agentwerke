using Autofac.Agents.Hooks;
using Autofac.Agents.Mcp;
using Autofac.Agents.Models;
using Autofac.Agents.Prompts;
using Autofac.Agents.Skills;
using Autofac.Agents.Tools;
using Autofac.Sandboxes;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autofac.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacAgents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var paths = AgentRegistryPaths.Resolve(configuration);
        services.AddSingleton(paths);
        services.AddSingleton<IAgentRegistry>(new FileAgentRegistry(paths.AgentsDirectory));
        services.AddSingleton<ISkillRepository>(new SkillRepository(paths.SkillsDirectory));
        services.AddSingleton<IAgentRegistryEditor, FileAgentRegistryEditor>();
        services.AddSingleton<IAgentPromptAssembler, AgentPromptAssembler>();
        services.AddScoped<IAgentTool, GitHubReadIssueTool>();
        services.AddScoped<IAgentTool, GitHubCreateBranchTool>();
        services.AddScoped<IAgentTool, GitHubCreatePullRequestTool>();
        services.AddScoped<IAgentTool, GitHubRequestReviewTool>();
        services.AddScoped<IAgentTool, GitHubPostReviewTool>();
        services.AddScoped<IAgentTool, CicdTriggerDeployTool>();
        services.AddScoped<IAgentTool, SandboxExecutionTool>();
        services.AddScoped<IAgentHookHandler, InternalPolicyHookHandler>();
        services.AddScoped<IAgentHookHandler, TemplateHookHandler>();
        services.AddScoped<IAgentHookGateway, HookGateway>();
        services.AddScoped<IMcpClientFactory, McpClientFactory>();
        services.AddScoped<IMcpToolSessionFactory, McpToolSessionFactory>();
        services.AddScoped<IToolRegistry, ToolRegistry>();
        services.AddScoped<IToolGateway, ToolGateway>();
        services.AddAutofacSandboxes(configuration);

        // Language model client — Anthropic if API key is present, null client otherwise
        services.Configure<LanguageModelOptions>(configuration.GetSection(LanguageModelOptions.Section));
        var apiKey = configuration[$"{LanguageModelOptions.Section}:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
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
        services.AddScoped<IAgentModelRunner, AgentModelRunner>();
        services.AddScoped<ISandboxedAgentRunner, OpenSandboxedAgentRunner>();

        services.AddScoped<IServiceTaskExecutor, AgentOrchestrator>();

        return services;
    }
}

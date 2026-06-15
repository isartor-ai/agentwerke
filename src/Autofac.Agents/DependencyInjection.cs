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

namespace Autofac.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacAgents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new SkillOptions();
        configuration.GetSection(SkillOptions.Section).Bind(options);

        var skillsDir = string.IsNullOrWhiteSpace(options.SkillsDirectory)
            ? options.SkillsDirectory
            : Path.IsPathRooted(options.SkillsDirectory)
                ? options.SkillsDirectory
                : Path.GetFullPath(options.SkillsDirectory);

        var manifests = MarkdownSkillLoader.LoadFromDirectory(skillsDir);
        var repository = new SkillRepository(manifests);

        services.AddSingleton<ISkillRepository>(repository);
        services.AddSingleton<IAgentPromptAssembler, AgentPromptAssembler>();
        services.AddScoped<IAgentTool, GitHubCreateBranchTool>();
        services.AddScoped<IAgentTool, GitHubCreatePullRequestTool>();
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
            services.AddScoped<ILanguageModelClient, AnthropicLanguageModelClient>();
        }
        else
        {
            services.AddScoped<ILanguageModelClient, NullLanguageModelClient>();
        }
        services.AddScoped<IAgentModelRunner, AgentModelRunner>();

        services.AddScoped<IServiceTaskExecutor, AgentOrchestrator>();

        return services;
    }
}

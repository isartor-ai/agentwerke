using Agentwerke.Integrations.Webhooks;
using Agentwerke.Integrations.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agentwerke.Integrations;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentwerkeIntegrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<IntegrationOptions>(o =>
            configuration.GetSection(IntegrationOptions.Section).Bind(o));

        services.AddScoped<ITriggerRouter, TagBasedTriggerRouter>();
        services.AddHttpClient<GitHubConnector>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.GitHub;
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Agentwerke/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });
        services.AddScoped<IGitHubConnector>(sp => sp.GetRequiredService<GitHubConnector>());
        services.AddScoped<IConnector>(sp => sp.GetRequiredService<GitHubConnector>());

        services.AddHttpClient<JiraConnector>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.Jira;
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddScoped<IJiraConnector>(sp => sp.GetRequiredService<JiraConnector>());
        services.AddScoped<IConnector>(sp => sp.GetRequiredService<JiraConnector>());

        services.AddHttpClient<SlackConnector>();
        services.AddScoped<ISlackConnector>(sp => sp.GetRequiredService<SlackConnector>());
        services.AddScoped<IConnector>(sp => sp.GetRequiredService<SlackConnector>());

        services.AddHttpClient<TeamsConnector>();
        services.AddScoped<ITeamsConnector>(sp => sp.GetRequiredService<TeamsConnector>());
        services.AddScoped<IConnector>(sp => sp.GetRequiredService<TeamsConnector>());

        services.AddScoped<IConnectorRegistry, ConnectorRegistry>();

        // Interaction channels (#215). Registered as IInteractionChannel so the provider-neutral
        // router in Agentwerke.Application collects them without naming any provider.
        services.AddHttpClient<TeamsInteractionChannel>();
        services.AddScoped<Application.Agents.IInteractionChannel>(sp =>
            sp.GetRequiredService<TeamsInteractionChannel>());

        // Approval-gate notifications fan out to the enabled chat connectors (#31).
        services.AddScoped<Application.Notifications.IApprovalNotifier, ConnectorApprovalNotifier>();

        return services;
    }
}

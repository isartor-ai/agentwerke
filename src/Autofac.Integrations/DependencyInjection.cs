using Autofac.Integrations.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autofac.Integrations;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacIntegrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<IntegrationOptions>(o =>
            configuration.GetSection(IntegrationOptions.Section).Bind(o));

        services.AddScoped<ITriggerRouter, TagBasedTriggerRouter>();
        services.AddHttpClient<IGitHubConnector, GitHubConnector>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<IntegrationOptions>>().Value.GitHub;
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Autofac/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });

        return services;
    }
}

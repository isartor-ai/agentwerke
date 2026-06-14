using Autofac.Integrations.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}

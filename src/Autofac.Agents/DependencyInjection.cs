using Autofac.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Autofac.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacAgents(this IServiceCollection services)
    {
        services.AddScoped<IServiceTaskExecutor, AgentOrchestrator>();
        return services;
    }
}

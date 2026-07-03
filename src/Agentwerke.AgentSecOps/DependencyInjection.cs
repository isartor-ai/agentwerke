using Microsoft.Extensions.DependencyInjection;

namespace Agentwerke.AgentSecOps;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentwerkeAgentSecOps(this IServiceCollection services)
    {
        services.AddScoped<IPolicyEvaluationService, PolicyEvaluationService>();
        services.AddSingleton<ISandboxProfileSelector, SandboxProfileSelector>();
        return services;
    }
}

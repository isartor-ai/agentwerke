using Microsoft.Extensions.DependencyInjection;

namespace Autofac.AgentSecOps;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacAgentSecOps(this IServiceCollection services)
    {
        services.AddScoped<IPolicyEvaluationService, PolicyEvaluationService>();
        services.AddSingleton<ISandboxProfileSelector, SandboxProfileSelector>();
        return services;
    }
}

using Agentwerke.Workflows.Bpmn;
using Agentwerke.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Agentwerke.Workflows;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentwerkeWorkflows(this IServiceCollection services)
    {
        services.AddScoped<IBpmnWorkflowValidator, BpmnWorkflowValidator>();
        services.AddScoped<IWorkflowEngineAdapter, WorkflowInstanceEngine>();
        return services;
    }
}

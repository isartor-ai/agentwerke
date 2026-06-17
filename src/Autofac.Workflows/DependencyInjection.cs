using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Autofac.Workflows;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacWorkflows(this IServiceCollection services)
    {
        services.AddScoped<IBpmnWorkflowValidator, BpmnWorkflowValidator>();
        services.AddScoped<ICamundaBpmnProjector, CamundaBpmnProjector>();
        services.AddScoped<IWorkflowEngineAdapter, WorkflowInstanceEngine>();
        return services;
    }
}

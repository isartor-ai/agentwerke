using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Autofac.Workflows;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacWorkflows(this IServiceCollection services)
    {
        services.AddScoped<IBpmnWorkflowValidator, BpmnWorkflowValidator>();
        services.AddScoped<IWorkflowInstanceEngine, WorkflowInstanceEngine>();
        return services;
    }
}
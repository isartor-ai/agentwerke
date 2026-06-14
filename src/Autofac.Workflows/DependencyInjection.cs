using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Camunda;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Autofac.Workflows;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacWorkflows(this IServiceCollection services)
    {
        services.AddScoped<IBpmnWorkflowValidator, BpmnWorkflowValidator>();
        services.AddScoped<IWorkflowEngineAdapter, WorkflowInstanceEngine>();
        services.AddSingleton<ICamunda8SpikeAnalyzer, Camunda8SpikeAnalyzer>();
        return services;
    }
}

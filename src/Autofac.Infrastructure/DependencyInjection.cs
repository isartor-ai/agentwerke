using Autofac.Application.Workflows;
using Autofac.Infrastructure.Persistence;
using Autofac.Infrastructure.Workflows;
using Autofac.Workflows.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autofac.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
        }

        services.AddDbContext<AutofacDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowValidationService, WorkflowValidationService>();
        services.AddScoped<IWorkflowAuthoringService, WorkflowAuthoringService>();
        services.AddScoped<IWorkflowRuntimeStore, WorkflowRuntimeStore>();

        return services;
    }
}

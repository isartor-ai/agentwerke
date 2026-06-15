using Autofac.Application.Observability;
using Autofac.Application.Secrets;
using Autofac.Application.Workflows;
using Autofac.Infrastructure.Persistence;
using Autofac.Infrastructure.Workers;
using Autofac.Infrastructure.Secrets;
using Autofac.Infrastructure.Workflows;
using Autofac.Workflows.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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

        var dataSource = new NpgsqlDataSourceBuilder(connectionString)
            .EnableDynamicJson()
            .Build();

        services.AddDbContext<AutofacDbContext>(options =>
            options.UseNpgsql(dataSource));

        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowValidationService, WorkflowValidationService>();
        services.AddScoped<IWorkflowAuthoringService, WorkflowAuthoringService>();
        services.AddScoped<IWorkflowRuntimeStore, WorkflowRuntimeStore>();
        services.AddScoped<IWorkflowRunRepository, WorkflowRunRepository>();
        services.AddScoped<IApprovalRepository, ApprovalRepository>();
        services.AddScoped<IWorkflowRunner, WorkflowRunnerAdapter>();
        services.AddScoped<IWorkflowRunOrchestrationService, WorkflowRunOrchestrationService>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddSingleton<ISecretStore, ConfigurationSecretStore>();

        services.AddScoped<IRunOutbox, OutboxRepository>();
        services.AddScoped<IWorkflowRunExecutor, WorkflowRunExecutor>();
        services.AddHostedService<RunDispatchWorker>();

        return services;
    }
}

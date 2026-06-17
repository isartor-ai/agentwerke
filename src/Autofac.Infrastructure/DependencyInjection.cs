using Autofac.Application.Observability;
using Autofac.Application.Secrets;
using Autofac.Application.Workflows;
using Autofac.AgentSecOps;
using Autofac.Infrastructure.Persistence;
using Autofac.Infrastructure.Policies;
using Autofac.Infrastructure.Secrets;
using Autofac.Infrastructure.Workers;
using Autofac.Infrastructure.Workflows;
using Autofac.Workflows.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        services.AddScoped<IWorkflowDeploymentService, CamundaWorkflowDeploymentService>();
        services.AddScoped<IWorkflowProcessStartService, CamundaWorkflowProcessStartService>();
        services.AddScoped<IWorkflowRuntimeStore, WorkflowRuntimeStore>();
        services.AddScoped<IWorkflowRunRepository, WorkflowRunRepository>();
        services.AddScoped<IRunContextRepository, RunContextRepository>();
        services.AddScoped<IApprovalRepository, ApprovalRepository>();
        services.AddScoped<IWorkflowRunner, WorkflowRunnerAdapter>();
        services.AddScoped<IWorkflowRunOrchestrationService, WorkflowRunOrchestrationService>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddSingleton<ISecretStore, ConfigurationSecretStore>();
        services.Configure<CamundaOptions>(o =>
            configuration.GetSection(CamundaOptions.Section).Bind(o));
        services.AddHttpClient<CamundaClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<CamundaOptions>>().Value;
            if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 10);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddScoped<ICamundaClient>(sp => sp.GetRequiredService<CamundaClient>());
        services.AddScoped<ICamundaRuntimeStatusService, CamundaRuntimeStatusService>();
        services.Configure<PolicyStoreOptions>(configuration.GetSection(PolicyStoreOptions.SectionName));
        services.AddSingleton<IPolicyRuleStore, FilePolicyRuleStore>();

        services.AddScoped<IRunOutbox, OutboxRepository>();
        services.AddScoped<IWorkflowRunExecutor, WorkflowRunExecutor>();
        services.AddHostedService<RunDispatchWorker>();

        return services;
    }
}

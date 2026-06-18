using Autofac.Agents;
using Autofac.AgentSecOps;
using Autofac.Api.Auth;
using Autofac.Api.Services;
using Autofac.Application.Workflows;
using Autofac.Infrastructure;
using Autofac.Integrations;
using Autofac.Observability;
using Autofac.Storage;
using Autofac.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddAutofacLogging();

builder.Services.AddControllers();
builder.Services.AddAutofacAuth(builder.Configuration, builder.Environment);
builder.Services.AddOpenApi("v1");
builder.Services.AddAutofacObservability(builder.Configuration);
builder.Services.AddAutofacInfrastructure(builder.Configuration);
builder.Services.AddAutofacStorage(builder.Configuration);
builder.Services.AddScoped<IEvidencePackService, EvidencePackService>();
builder.Services.AddAutofacWorkflows();
builder.Services.AddAutofacAgentSecOps();
builder.Services.AddAutofacAgents(builder.Configuration);
builder.Services.AddAutofacIntegrations(builder.Configuration);

var app = builder.Build();

app.UseAutofacObservability();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapPrometheusScrapingEndpoint();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

using Agentwerke.Agents;
using Agentwerke.AgentSecOps;
using Agentwerke.Api.Auth;
using Agentwerke.Api.Settings;
using Agentwerke.Api.Services;
using Agentwerke.Application.Workflows;
using Agentwerke.Infrastructure;
using Agentwerke.Integrations;
using Agentwerke.Observability;
using Agentwerke.Storage;
using Agentwerke.Workflows;

var builder = WebApplication.CreateBuilder(args);

var settingsFilePaths = builder.Configuration.AddAgentwerkeSettingsConfiguration(builder.Environment);

builder.Logging.AddAgentwerkeLogging();

builder.Services.AddControllers();
builder.Services.AddAgentwerkeAuth(builder.Configuration, builder.Environment);
builder.Services.AddOpenApi("v1");
builder.Services.AddAgentwerkeSettings(builder.Configuration, settingsFilePaths);
builder.Services.AddAgentwerkeObservability(builder.Configuration);
builder.Services.AddAgentwerkeInfrastructure(builder.Configuration);
builder.Services.AddAgentwerkeStorage(builder.Configuration);
builder.Services.AddScoped<IEvidencePackService, EvidencePackService>();
builder.Services.AddAgentwerkeWorkflows();
builder.Services.AddAgentwerkeAgentSecOps();
builder.Services.AddAgentwerkeAgents(builder.Configuration);
builder.Services.AddAgentwerkeIntegrations(builder.Configuration);

var app = builder.Build();

app.UseAgentwerkeObservability();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapPrometheusScrapingEndpoint();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

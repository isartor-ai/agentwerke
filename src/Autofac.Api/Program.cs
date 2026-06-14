using Autofac.Agents;
using Autofac.AgentSecOps;
using Autofac.Infrastructure;
using Autofac.Integrations;
using Autofac.Storage;
using Autofac.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddOpenApi("v1");
builder.Services.AddAutofacInfrastructure(builder.Configuration);
builder.Services.AddAutofacStorage(builder.Configuration);
builder.Services.AddAutofacWorkflows();
builder.Services.AddAutofacAgentSecOps();
builder.Services.AddAutofacAgents(builder.Configuration);
builder.Services.AddAutofacIntegrations(builder.Configuration);

var app = builder.Build();

app.MapOpenApi("/openapi/{documentName}.json");

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

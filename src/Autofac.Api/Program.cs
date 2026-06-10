using Autofac.Infrastructure;
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

var app = builder.Build();

app.MapOpenApi("/openapi/{documentName}.json");

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

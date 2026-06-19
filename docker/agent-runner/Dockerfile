FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /repo

COPY global.json Directory.Build.props Autofac.sln ./
COPY src/Autofac.Domain/Autofac.Domain.csproj src/Autofac.Domain/
COPY src/Autofac.Application/Autofac.Application.csproj src/Autofac.Application/
COPY src/Autofac.Workflows/Autofac.Workflows.csproj src/Autofac.Workflows/
COPY src/Autofac.Infrastructure/Autofac.Infrastructure.csproj src/Autofac.Infrastructure/
COPY src/Autofac.Storage/Autofac.Storage.csproj src/Autofac.Storage/
COPY src/Autofac.Sandboxes/Autofac.Sandboxes.csproj src/Autofac.Sandboxes/
COPY src/Autofac.Agents/Autofac.Agents.csproj src/Autofac.Agents/
COPY src/Autofac.Integrations/Autofac.Integrations.csproj src/Autofac.Integrations/
COPY src/Autofac.AgentSecOps/Autofac.AgentSecOps.csproj src/Autofac.AgentSecOps/
COPY src/Autofac.AgentRunner/Autofac.AgentRunner.csproj src/Autofac.AgentRunner/
RUN dotnet restore src/Autofac.AgentRunner/Autofac.AgentRunner.csproj

COPY src/ src/
RUN dotnet publish src/Autofac.AgentRunner/Autofac.AgentRunner.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /output
ENTRYPOINT ["dotnet", "Autofac.AgentRunner.dll"]

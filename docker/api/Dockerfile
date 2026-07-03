FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /repo

# Restore — copy only project files first for layer caching
COPY global.json Directory.Build.props ./
COPY .config/ .config/
RUN dotnet tool restore
COPY src/Agentwerke.Domain/Agentwerke.Domain.csproj                         src/Agentwerke.Domain/
COPY src/Agentwerke.Application/Agentwerke.Application.csproj               src/Agentwerke.Application/
COPY src/Agentwerke.Workflows/Agentwerke.Workflows.csproj                   src/Agentwerke.Workflows/
COPY src/Agentwerke.Infrastructure/Agentwerke.Infrastructure.csproj         src/Agentwerke.Infrastructure/
COPY src/Agentwerke.Storage/Agentwerke.Storage.csproj                       src/Agentwerke.Storage/
COPY src/Agentwerke.Sandboxes/Agentwerke.Sandboxes.csproj                   src/Agentwerke.Sandboxes/
COPY src/Agentwerke.Agents/Agentwerke.Agents.csproj                         src/Agentwerke.Agents/
COPY src/Agentwerke.Integrations/Agentwerke.Integrations.csproj             src/Agentwerke.Integrations/
COPY src/Agentwerke.AgentSecOps/Agentwerke.AgentSecOps.csproj               src/Agentwerke.AgentSecOps/
COPY src/Agentwerke.Observability/Agentwerke.Observability.csproj           src/Agentwerke.Observability/
COPY src/Agentwerke.Api/Agentwerke.Api.csproj                               src/Agentwerke.Api/
RUN dotnet restore src/Agentwerke.Api/Agentwerke.Api.csproj

# Build
COPY src/ src/
RUN dotnet publish src/Agentwerke.Api/Agentwerke.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Skills directory (mounted at runtime via volume)
RUN mkdir -p /app/skills /app/storage

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080
ENTRYPOINT ["dotnet", "Agentwerke.Api.dll"]

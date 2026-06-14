FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /repo

# Restore — copy only project files first for layer caching
COPY global.json Directory.Build.props ./
COPY .config/ .config/
RUN dotnet tool restore
COPY src/Autofac.Domain/Autofac.Domain.csproj                         src/Autofac.Domain/
COPY src/Autofac.Application/Autofac.Application.csproj               src/Autofac.Application/
COPY src/Autofac.Workflows/Autofac.Workflows.csproj                   src/Autofac.Workflows/
COPY src/Autofac.Infrastructure/Autofac.Infrastructure.csproj         src/Autofac.Infrastructure/
COPY src/Autofac.Storage/Autofac.Storage.csproj                       src/Autofac.Storage/
COPY src/Autofac.Sandboxes/Autofac.Sandboxes.csproj                   src/Autofac.Sandboxes/
COPY src/Autofac.Agents/Autofac.Agents.csproj                         src/Autofac.Agents/
COPY src/Autofac.Integrations/Autofac.Integrations.csproj             src/Autofac.Integrations/
COPY src/Autofac.AgentSecOps/Autofac.AgentSecOps.csproj               src/Autofac.AgentSecOps/
COPY src/Autofac.Observability/Autofac.Observability.csproj           src/Autofac.Observability/
COPY src/Autofac.Api/Autofac.Api.csproj                               src/Autofac.Api/
RUN dotnet restore src/Autofac.Api/Autofac.Api.csproj

# Build
COPY src/ src/
RUN dotnet publish src/Autofac.Api/Autofac.Api.csproj \
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
ENTRYPOINT ["dotnet", "Autofac.Api.dll"]

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /repo

COPY global.json Directory.Build.props Agentwerke.sln ./
COPY src/Agentwerke.Domain/Agentwerke.Domain.csproj src/Agentwerke.Domain/
COPY src/Agentwerke.Application/Agentwerke.Application.csproj src/Agentwerke.Application/
COPY src/Agentwerke.Workflows/Agentwerke.Workflows.csproj src/Agentwerke.Workflows/
COPY src/Agentwerke.Infrastructure/Agentwerke.Infrastructure.csproj src/Agentwerke.Infrastructure/
COPY src/Agentwerke.Storage/Agentwerke.Storage.csproj src/Agentwerke.Storage/
COPY src/Agentwerke.Sandboxes/Agentwerke.Sandboxes.csproj src/Agentwerke.Sandboxes/
COPY src/Agentwerke.Agents/Agentwerke.Agents.csproj src/Agentwerke.Agents/
COPY src/Agentwerke.Integrations/Agentwerke.Integrations.csproj src/Agentwerke.Integrations/
COPY src/Agentwerke.AgentSecOps/Agentwerke.AgentSecOps.csproj src/Agentwerke.AgentSecOps/
COPY src/Agentwerke.AgentRunner/Agentwerke.AgentRunner.csproj src/Agentwerke.AgentRunner/
RUN dotnet restore src/Agentwerke.AgentRunner/Agentwerke.AgentRunner.csproj

COPY src/ src/
RUN dotnet publish src/Agentwerke.AgentRunner/Agentwerke.AgentRunner.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app
# git is required by the sandbox.git tool (#140) — clone/checkout/commit/push for
# implementation/review/test agents. Workflow authors needing other language toolchains
# (npm, python, etc.) should override AgentProfile.DockerImage with a custom image.
RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN mkdir -p /output
ENTRYPOINT ["dotnet", "Agentwerke.AgentRunner.dll"]

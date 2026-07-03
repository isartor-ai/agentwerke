FROM mcr.microsoft.com/dotnet/sdk:9.0 AS test-runner
WORKDIR /repo

# Restore — only the test project's csproj needed (no project references)
COPY global.json Directory.Build.props ./
COPY tests/Agentwerke.E2ETests/Agentwerke.E2ETests.csproj tests/Agentwerke.E2ETests/
RUN dotnet restore tests/Agentwerke.E2ETests/Agentwerke.E2ETests.csproj

# Copy test sources and build
COPY tests/Agentwerke.E2ETests/ tests/Agentwerke.E2ETests/
RUN dotnet build tests/Agentwerke.E2ETests/Agentwerke.E2ETests.csproj -c Release --no-restore

# ENV vars (AGENTWERKE_API_URL, CAMUNDA_ENABLED, etc.) are injected by compose at runtime
ENTRYPOINT ["dotnet", "test", "tests/Agentwerke.E2ETests/Agentwerke.E2ETests.csproj", \
            "-c", "Release", "--no-build", "--logger", "console;verbosity=normal"]

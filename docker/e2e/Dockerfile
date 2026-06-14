FROM mcr.microsoft.com/dotnet/sdk:9.0 AS test-runner
WORKDIR /repo

# Restore — only the test project's csproj needed (no project references)
COPY global.json Directory.Build.props ./
COPY tests/Autofac.E2ETests/Autofac.E2ETests.csproj tests/Autofac.E2ETests/
RUN dotnet restore tests/Autofac.E2ETests/Autofac.E2ETests.csproj

# Copy test sources and build
COPY tests/Autofac.E2ETests/ tests/Autofac.E2ETests/
RUN dotnet build tests/Autofac.E2ETests/Autofac.E2ETests.csproj -c Release --no-restore

# ENV vars (AUTOFAC_API_URL, CAMUNDA_ENABLED, etc.) are injected by compose at runtime
ENTRYPOINT ["dotnet", "test", "tests/Autofac.E2ETests/Autofac.E2ETests.csproj", \
            "-c", "Release", "--no-build", "--logger", "console;verbosity=normal"]

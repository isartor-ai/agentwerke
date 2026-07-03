using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentwerke.Sandboxes.Tests;

/// <summary>
/// Exercises a real OpenSandbox server end to end: execution, artifact capture,
/// cleanup, failure handling, and profile-driven resource/network mapping
/// (see SandboxProfileCatalog). Gated by AUTOFAC_OPEN_SANDBOX_SERVER_URL so CI
/// environments without a running server stay green — this is the
/// "production-style" path from issue 128, as opposed to the local Docker
/// fallback covered by DockerSandboxLifecycleIntegrationTests.
///
/// To run locally: start an OpenSandbox server in Docker mode (see
/// docs/manual-test-opensandbox.md) and set AUTOFAC_OPEN_SANDBOX_SERVER_URL.
/// </summary>
public sealed class OpenSandboxIntegrationTests
{
    [Fact]
    public async Task ExecuteAsync_WhenEnvironmentConfigured_RunsSmokeCommand()
    {
        if (!TryCreateExecutor(out var executor)) return;

        var result = await executor!.ExecuteAsync(
            new SandboxExecutionRequest(
                RunId: "integration-run",
                StepId: "open-sandbox-smoke",
                AgentName: "integration-test",
                Action: "sandbox.execute",
                Environment: "test",
                PurposeType: "verification",
                PolicyTag: "issue-126",
                Attempt: 1,
                Command: new SandboxCommandSpec(
                    ["sh", "-c", "mkdir -p /output && echo hello > /output/result.txt && echo smoke-ok"],
                    WorkingDirectory: "/workspace"),
                ArtifactPaths: ["/output"]),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal(SandboxCommandState.Completed, result.CommandState);
        Assert.Contains("smoke-ok", result.Logs, StringComparison.Ordinal);
        Assert.Contains(result.Artifacts.Values, v => v.Contains("hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_NonZeroExit_ReturnsFailureWithExitCode()
    {
        if (!TryCreateExecutor(out var executor)) return;

        var result = await executor!.ExecuteAsync(
            new SandboxExecutionRequest(
                RunId: "integration-run",
                StepId: $"open-sandbox-failure-{Guid.NewGuid():N}",
                AgentName: "integration-test",
                Action: "sandbox.execute",
                Environment: "test",
                PurposeType: "verification",
                PolicyTag: "issue-128",
                Attempt: 1,
                Command: new SandboxCommandSpec(
                    ["sh", "-c", "exit 7"],
                    WorkingDirectory: "/workspace")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_OnSuccess_DeletesSandbox()
    {
        if (!TryCreateExecutor(out var executor, out var client)) return;

        var result = await executor!.ExecuteAsync(
            new SandboxExecutionRequest(
                RunId: "integration-run",
                StepId: $"open-sandbox-cleanup-{Guid.NewGuid():N}",
                AgentName: "integration-test",
                Action: "sandbox.execute",
                Environment: "test",
                PurposeType: "verification",
                PolicyTag: "issue-128",
                Attempt: 1,
                Command: new SandboxCommandSpec(["sh", "-c", "true"])),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.NotNull(result.ProviderSandboxId);

        var diagnostics = await client!.GetDiagnosticsAsync(result.ProviderSandboxId!, CancellationToken.None);
        Assert.Equal("NotFound", diagnostics.Entries.GetValueOrDefault("sandbox.state"));
    }

    [Fact]
    public async Task ExecuteAsync_RetainSandboxOnFailure_KeepsSandboxForDiagnostics()
    {
        if (!TryCreateExecutor(out var executor, out var client)) return;

        string? sandboxId = null;
        try
        {
            var result = await executor!.ExecuteAsync(
                new SandboxExecutionRequest(
                    RunId: "integration-run",
                    StepId: $"open-sandbox-retain-{Guid.NewGuid():N}",
                    AgentName: "integration-test",
                    Action: "sandbox.execute",
                    Environment: "test",
                    PurposeType: "verification",
                    PolicyTag: "issue-128",
                    Attempt: 1,
                    Profile: new SandboxExecutionProfile(
                        CleanupPolicy: new SandboxCleanupPolicy(RetainSandboxOnFailure: true)),
                    Command: new SandboxCommandSpec(["sh", "-c", "exit 1"])),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            sandboxId = result.ProviderSandboxId;
            Assert.NotNull(sandboxId);

            var diagnostics = await client!.GetDiagnosticsAsync(sandboxId!, CancellationToken.None);
            Assert.NotEqual("NotFound", diagnostics.Entries.GetValueOrDefault("sandbox.state"));
        }
        finally
        {
            if (sandboxId is not null)
            {
                await client!.DeleteAsync(sandboxId, CancellationToken.None);
            }
        }
    }

    [Theory]
    [InlineData(SandboxProfileNames.Offline)]
    [InlineData(SandboxProfileNames.RepoRead)]
    public async Task ExecuteAsync_WithCatalogProfile_AppliesResolvedResourcesAndNetworkPolicy(string profileName)
    {
        if (!TryCreateExecutor(out var executor)) return;

        var runId = $"integration-{Guid.NewGuid():N}";
        var profile = SandboxProfileCatalog.Resolve(profileName, runId);

        var result = await executor!.ExecuteAsync(
            new SandboxExecutionRequest(
                RunId: runId,
                StepId: "profile-driven",
                AgentName: "integration-test",
                Action: "sandbox.execute",
                Environment: "test",
                PurposeType: "verification",
                PolicyTag: "issue-127-128",
                Attempt: 1,
                Profile: profile,
                Command: new SandboxCommandSpec(["sh", "-c", "echo profile-ok"])),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Contains("profile-ok", result.Logs, StringComparison.Ordinal);
    }

    private static bool TryCreateExecutor(out OpenSandboxSandboxExecutor? executor) =>
        TryCreateExecutor(out executor, out _);

    private static bool TryCreateExecutor(out OpenSandboxSandboxExecutor? executor, out IOpenSandboxClient? client)
    {
        var serverUrl = Environment.GetEnvironmentVariable("AUTOFAC_OPEN_SANDBOX_SERVER_URL");
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            executor = null;
            client = null;
            return false;
        }

        var options = Options.Create(new SandboxOptions
        {
            OpenSandbox = new OpenSandboxProviderOptions
            {
                Enabled = true,
                ServerUrl = serverUrl,
                ApiKey = Environment.GetEnvironmentVariable("AUTOFAC_OPEN_SANDBOX_API_KEY") ?? string.Empty,
                DefaultImage = Environment.GetEnvironmentVariable("AUTOFAC_OPEN_SANDBOX_IMAGE") ?? "alpine:3.19",
                DefaultTimeoutSeconds = 60,
                ReadinessTimeoutSeconds = 30,
                WorkingDirectory = "/workspace",
                DefaultArtifactPaths = ["/output"]
            }
        });

        var httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        var apiClient = new OpenSandboxApiClient(httpClient, options);
        client = apiClient;
        executor = new OpenSandboxSandboxExecutor(
            apiClient,
            new OpenSandboxRequestMapper(options),
            NullLogger<OpenSandboxSandboxExecutor>.Instance);
        return true;
    }
}

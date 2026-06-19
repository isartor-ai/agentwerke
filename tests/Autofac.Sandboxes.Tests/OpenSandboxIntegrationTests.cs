using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autofac.Sandboxes.Tests;

public sealed class OpenSandboxIntegrationTests
{
    [Fact]
    public async Task ExecuteAsync_WhenEnvironmentConfigured_RunsSmokeCommand()
    {
        var serverUrl = Environment.GetEnvironmentVariable("AUTOFAC_OPEN_SANDBOX_SERVER_URL");
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return;
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

        using var httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        var executor = new OpenSandboxSandboxExecutor(
            new OpenSandboxApiClient(httpClient, options),
            new OpenSandboxRequestMapper(options),
            NullLogger<OpenSandboxSandboxExecutor>.Instance);

        var result = await executor.ExecuteAsync(
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
        Assert.Contains("hello", result.Artifacts.Values, StringComparer.Ordinal);
    }
}

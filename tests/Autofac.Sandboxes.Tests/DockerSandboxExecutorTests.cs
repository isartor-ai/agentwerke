using Autofac.Sandboxes;
using Docker.DotNet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autofac.Sandboxes.Tests;

/// <summary>
/// Tests the DockerSandboxExecutor contract-level behavior — specifically the
/// path where Docker is unavailable, which must produce a clean failure result
/// rather than throwing unhandled exceptions.
/// </summary>
public sealed class DockerSandboxExecutorTests
{
    private static SandboxExecutionRequest MakeRequest(string stepId = "step-001") =>
        new(
            RunId: "run-abc",
            StepId: stepId,
            AgentName: "deploy-agent",
            Action: "deploy",
            Environment: "staging",
            PurposeType: "deployment",
            PolicyTag: "deploy-staging",
            Attempt: 1);

    private static DockerSandboxExecutor MakeExecutor(SandboxOptions? opts = null)
    {
        opts ??= new SandboxOptions { Enabled = true, TimeoutSeconds = 5 };

        // Point at a definitely-unreachable Docker socket so the test stays offline.
        var fakeUri = new Uri("tcp://127.0.0.1:1");
        var client = new DockerClientConfiguration(fakeUri).CreateClient();
        return new DockerSandboxExecutor(
            client,
            Options.Create(opts),
            NullLogger<DockerSandboxExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDockerUnavailable_ReturnsFail_NotThrow()
    {
        var executor = MakeExecutor();
        var req = MakeRequest();

        var result = await executor.ExecuteAsync(req, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("Container creation failed", result.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDockerUnavailable_ResultHasEmptyArtifacts()
    {
        var executor = MakeExecutor();
        var req = MakeRequest();

        var result = await executor.ExecuteAsync(req, CancellationToken.None);

        Assert.NotNull(result.Artifacts);
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDockerUnavailable_DurationIsPopulated()
    {
        var executor = MakeExecutor();
        var req = MakeRequest();

        var result = await executor.ExecuteAsync(req, CancellationToken.None);

        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public void SandboxOptions_Section_IsCorrect()
    {
        Assert.Equal("Sandboxes:Docker", SandboxOptions.Section);
    }
}

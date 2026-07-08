using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentwerke.Sandboxes.Tests;

/// <summary>
/// Exercises the real local-fallback Docker provider end to end: execution,
/// artifact capture, cleanup, and failure handling. Gated by
/// AGENTWERKE_DOCKER_SANDBOX_E2E=1 so CI environments without a Docker daemon
/// stay green — mirrors the gating pattern in OpenSandboxIntegrationTests.
/// This is the "local fallback" path from issue 128: Agentwerke talking directly
/// to Docker, without an OpenSandbox server in front of it.
/// </summary>
public sealed class DockerSandboxLifecycleIntegrationTests
{
    private static bool IsEnabled =>
        Environment.GetEnvironmentVariable("AGENTWERKE_DOCKER_SANDBOX_E2E") == "1";

    [Fact]
    public async Task ExecuteAsync_RealContainer_CapturesArtifactsAndRemovesContainer()
    {
        if (!IsEnabled) return;

        using var docker = CreateDockerClient();
        var artifactsRoot = CreateTempArtifactsRoot();
        try
        {
            var executor = CreateExecutor(docker, artifactsRoot);
            var request = new SandboxExecutionRequest(
                RunId: "e2e-run",
                StepId: $"lifecycle-{Guid.NewGuid():N}",
                AgentName: "deploy-agent",
                Action: "deploy",
                Environment: "staging",
                PurposeType: "deployment",
                PolicyTag: "deploy-staging",
                Attempt: 1,
                Image: "alpine:3.19",
                Command: new SandboxCommandSpec(
                    ["sh", "-c", "mkdir -p /output && echo hello > /output/result.txt && exit 0"]));

            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(SandboxCommandState.Completed, result.CommandState);
            Assert.Contains(result.Artifacts.Values, v => v.Contains("hello", StringComparison.Ordinal));
            Assert.NotNull(result.ProviderSandboxId);

            await AssertContainerRemovedAsync(docker, result.ProviderSandboxId!);
        }
        finally
        {
            CleanupArtifactsRoot(artifactsRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithRestrictedNetworkPolicy_GrantsNetworkAccess()
    {
        if (!IsEnabled) return;

        using var docker = CreateDockerClient();
        var artifactsRoot = CreateTempArtifactsRoot();
        try
        {
            var executor = CreateExecutor(docker, artifactsRoot);
            var request = new SandboxExecutionRequest(
                RunId: "e2e-run",
                StepId: $"lifecycle-network-{Guid.NewGuid():N}",
                AgentName: "agent-sandboxed-test",
                Action: "run-agent",
                Environment: "ci",
                PurposeType: "verification",
                PolicyTag: "agent-sandboxed-e2e",
                Attempt: 1,
                Image: "alpine:3.19",
                Profile: new SandboxExecutionProfile(
                    NetworkPolicy: new SandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, ["wiremock"])),
                // /sys/class/net needs no extra tooling: it always lists "lo" (loopback) plus
                // one non-loopback interface per network the container is attached to.
                Command: new SandboxCommandSpec(
                    ["sh", "-c", "mkdir -p /output && ls /sys/class/net | tee /output/interfaces.txt"]));

            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.True(result.Succeeded, result.FailureReason);
            var interfaces = result.Artifacts["interfaces.txt"];
            Assert.Contains("lo", interfaces.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            Assert.True(
                interfaces.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length > 1,
                $"Expected a non-loopback network interface for a Restricted network policy. Interfaces: {interfaces}");
        }
        finally
        {
            CleanupArtifactsRoot(artifactsRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithNoneNetworkPolicy_HasNoNetworkInterface()
    {
        if (!IsEnabled) return;

        using var docker = CreateDockerClient();
        var artifactsRoot = CreateTempArtifactsRoot();
        try
        {
            var executor = CreateExecutor(docker, artifactsRoot);
            var request = new SandboxExecutionRequest(
                RunId: "e2e-run",
                StepId: $"lifecycle-no-network-{Guid.NewGuid():N}",
                AgentName: "agent-sandboxed-test",
                Action: "run-agent",
                Environment: "ci",
                PurposeType: "verification",
                PolicyTag: "agent-sandboxed-e2e",
                Attempt: 1,
                Image: "alpine:3.19",
                Profile: new SandboxExecutionProfile(
                    NetworkPolicy: new SandboxNetworkPolicy(SandboxNetworkAccessMode.None)),
                Command: new SandboxCommandSpec(
                    ["sh", "-c", "mkdir -p /output && ls /sys/class/net | tee /output/interfaces.txt"]));

            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.True(result.Succeeded, result.FailureReason);
            var interfaces = result.Artifacts["interfaces.txt"]
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(["lo"], interfaces);
        }
        finally
        {
            CleanupArtifactsRoot(artifactsRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DefaultNetwork_InjectsHostDockerInternalAlias()
    {
        if (!IsEnabled) return;

        using var docker = CreateDockerClient();
        var artifactsRoot = CreateTempArtifactsRoot();
        try
        {
            var executor = CreateExecutor(docker, artifactsRoot);
            var request = new SandboxExecutionRequest(
                RunId: "e2e-run",
                StepId: $"lifecycle-host-alias-{Guid.NewGuid():N}",
                AgentName: "developer",
                Action: "implementation.build",
                Environment: "ci",
                PurposeType: "implementation",
                PolicyTag: "agent-sandboxed-e2e",
                Attempt: 1,
                Image: "alpine:3.19",
                Command: new SandboxCommandSpec(
                    ["sh", "-c", "mkdir -p /output && cat /etc/hosts | tee /output/hosts.txt"]));

            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Contains("host.docker.internal", result.Artifacts["hosts.txt"]);
        }
        finally
        {
            CleanupArtifactsRoot(artifactsRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NonZeroExit_ReturnsFailureWithExitCodeAndKeepsArtifacts()
    {
        if (!IsEnabled) return;

        using var docker = CreateDockerClient();
        var artifactsRoot = CreateTempArtifactsRoot();
        try
        {
            var executor = CreateExecutor(docker, artifactsRoot);
            var request = new SandboxExecutionRequest(
                RunId: "e2e-run",
                StepId: $"lifecycle-failure-{Guid.NewGuid():N}",
                AgentName: "deploy-agent",
                Action: "deploy",
                Environment: "staging",
                PurposeType: "deployment",
                PolicyTag: "deploy-staging",
                Attempt: 1,
                Image: "alpine:3.19",
                Command: new SandboxCommandSpec(
                    ["sh", "-c", "mkdir -p /output && echo partial > /output/partial.txt && exit 7"]));

            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(7, result.ExitCode);
            Assert.Equal(SandboxCommandState.Failed, result.CommandState);
            Assert.Contains("exited with code 7", result.FailureReason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.Artifacts.Values, v => v.Contains("partial", StringComparison.Ordinal));

            await AssertContainerRemovedAsync(docker, result.ProviderSandboxId!);
        }
        finally
        {
            CleanupArtifactsRoot(artifactsRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RealContainer_StreamsLogsBeforeContainerCompletes()
    {
        if (!IsEnabled) return;

        using var docker = CreateDockerClient();
        var artifactsRoot = CreateTempArtifactsRoot();
        try
        {
            var executor = CreateExecutor(docker, artifactsRoot);
            var request = new SandboxExecutionRequest(
                RunId: "e2e-run",
                StepId: $"lifecycle-stream-{Guid.NewGuid():N}",
                AgentName: "developer",
                Action: "implementation.build",
                Environment: "ci",
                PurposeType: "implementation",
                PolicyTag: "agent-sandboxed-e2e",
                Attempt: 1,
                Image: "alpine:3.19",
                Command: new SandboxCommandSpec(
                    ["sh", "-c", "echo live-start && sleep 2 && echo live-end && mkdir -p /output && echo done > /output/result.txt"]));

            var firstLogSeen = new TaskCompletionSource<SandboxLogEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
            var executionTask = executor.ExecuteAsync(
                request,
                CancellationToken.None,
                (entry, _) =>
                {
                    if (entry.Message.Contains("live-start", StringComparison.Ordinal))
                    {
                        firstLogSeen.TrySetResult(entry);
                    }

                    return Task.CompletedTask;
                });

            var completed = await Task.WhenAny(firstLogSeen.Task, executionTask, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.Same(firstLogSeen.Task, completed);
            Assert.False(executionTask.IsCompleted, "The first log line should arrive before the container exits.");

            var result = await executionTask;
            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Contains(result.StructuredLogs ?? [], entry => entry.Message.Contains("live-start", StringComparison.Ordinal));
            Assert.Contains(result.StructuredLogs ?? [], entry => entry.Message.Contains("live-end", StringComparison.Ordinal));
        }
        finally
        {
            CleanupArtifactsRoot(artifactsRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RetainSandboxOnFailure_KeepsContainerForDiagnostics()
    {
        if (!IsEnabled) return;

        using var docker = CreateDockerClient();
        var artifactsRoot = CreateTempArtifactsRoot();
        string? containerId = null;
        try
        {
            var executor = CreateExecutor(docker, artifactsRoot);
            var request = new SandboxExecutionRequest(
                RunId: "e2e-run",
                StepId: $"lifecycle-retain-{Guid.NewGuid():N}",
                AgentName: "deploy-agent",
                Action: "deploy",
                Environment: "staging",
                PurposeType: "deployment",
                PolicyTag: "deploy-staging",
                Attempt: 1,
                Image: "alpine:3.19",
                Profile: new SandboxExecutionProfile(
                    CleanupPolicy: new SandboxCleanupPolicy(RetainSandboxOnFailure: true)),
                Command: new SandboxCommandSpec(["sh", "-c", "exit 1"]));

            var result = await executor.ExecuteAsync(request, CancellationToken.None);

            Assert.False(result.Succeeded);
            containerId = result.ProviderSandboxId;
            Assert.NotNull(containerId);

            // Cleanup policy said "retain on failure" — the container must still exist.
            var inspect = await docker.Containers.InspectContainerAsync(containerId);
            Assert.NotNull(inspect);
        }
        finally
        {
            if (containerId is not null)
            {
                await docker.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true });
            }

            CleanupArtifactsRoot(artifactsRoot);
        }
    }

    private static async Task AssertContainerRemovedAsync(IDockerClient docker, string containerId)
    {
        try
        {
            await docker.Containers.InspectContainerAsync(containerId);
            Assert.Fail($"Container {containerId} should have been removed by the executor's cleanup policy.");
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Expected: the container is gone.
        }
    }

    private static DockerSandboxExecutor CreateExecutor(IDockerClient docker, string artifactsRoot) =>
        new(
            docker,
            Options.Create(new SandboxOptions
            {
                Enabled = true,
                TimeoutSeconds = 30,
                ArtifactsHostPath = artifactsRoot
            }),
            NullLogger<DockerSandboxExecutor>.Instance);

    private static IDockerClient CreateDockerClient()
    {
        var endpoint = Environment.GetEnvironmentVariable("AGENTWERKE_DOCKER_ENDPOINT") ?? "unix:///var/run/docker.sock";
        return new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
    }

    private static string CreateTempArtifactsRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agentwerke-sandbox-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupArtifactsRoot(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; not test-critical.
        }
    }
}

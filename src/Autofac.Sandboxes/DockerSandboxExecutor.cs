using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofac.Sandboxes;

/// <summary>
/// Runs one agent task inside an ephemeral Docker container, captures logs and
/// artifacts from /output, then removes the container.
/// </summary>
public sealed class DockerSandboxExecutor : ISandboxExecutor, IAsyncDisposable
{
    private readonly IDockerClient _docker;
    private readonly SandboxOptions _options;
    private readonly ILogger<DockerSandboxExecutor> _logger;

    public DockerSandboxExecutor(
        IDockerClient docker,
        IOptions<SandboxOptions> options,
        ILogger<DockerSandboxExecutor> logger)
    {
        _docker = docker;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var image = request.Image ?? _options.DefaultImage;
        var artifactsDir = ResolveArtifactsDir(request.StepId);
        Directory.CreateDirectory(artifactsDir);

        _logger.LogInformation(
            "Sandbox start run={RunId} step={StepId} agent={Agent} image={Image}",
            request.RunId, request.StepId, request.AgentName, image);

        string containerId;
        try
        {
            containerId = await CreateContainerAsync(request, image, artifactsDir, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create container for step {StepId}", request.StepId);
            return Failure($"Container creation failed: {ex.Message}", started);
        }

        try
        {
            return await RunAndCollectAsync(request, containerId, artifactsDir, started, cancellationToken);
        }
        finally
        {
            await RemoveContainerQuietlyAsync(containerId);
        }
    }

    private async Task<string> CreateContainerAsync(
        SandboxExecutionRequest request,
        string image,
        string artifactsDir,
        CancellationToken cancellationToken)
    {
        var env = new List<string>
        {
            $"AUTOFAC_RUN_ID={request.RunId}",
            $"AUTOFAC_STEP_ID={request.StepId}",
            $"AUTOFAC_AGENT={request.AgentName}",
            $"AUTOFAC_ACTION={request.Action}",
            $"AUTOFAC_ENVIRONMENT={request.Environment ?? string.Empty}",
            $"AUTOFAC_PURPOSE={request.PurposeType}",
            $"AUTOFAC_POLICY={request.PolicyTag}",
            $"AUTOFAC_ATTEMPT={request.Attempt}",
        };

        // MVP entry point: write a structured result file and echo to stdout.
        const string entrypoint = """
            set -e
            echo "autofac-sandbox: starting task"
            echo "agent=$AUTOFAC_AGENT action=$AUTOFAC_ACTION env=$AUTOFAC_ENVIRONMENT attempt=$AUTOFAC_ATTEMPT"
            mkdir -p /output
            cat > /output/result.json <<EOF
            {
              "runId": "$AUTOFAC_RUN_ID",
              "stepId": "$AUTOFAC_STEP_ID",
              "agent": "$AUTOFAC_AGENT",
              "action": "$AUTOFAC_ACTION",
              "environment": "$AUTOFAC_ENVIRONMENT",
              "status": "completed",
              "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
            }
            EOF
            echo "autofac-sandbox: task complete"
            """;

        var createParams = new CreateContainerParameters
        {
            Image = image,
            Cmd = ["sh", "-c", entrypoint],
            Env = env,
            HostConfig = new HostConfig
            {
                Memory = (long)_options.MemoryLimitMb * 1024 * 1024,
                CPUQuota = _options.CpuQuota,
                CPUPeriod = 100_000,
                NetworkMode = "none",
                AutoRemove = false,
                Binds = [$"{artifactsDir}:/output:rw"],
            },
            Labels = new Dictionary<string, string>
            {
                ["autofac.run"] = request.RunId,
                ["autofac.step"] = request.StepId,
            },
        };

        var response = await _docker.Containers.CreateContainerAsync(createParams, cancellationToken);
        return response.ID;
    }

    private async Task<SandboxExecutionResult> RunAndCollectAsync(
        SandboxExecutionRequest request,
        string containerId,
        string artifactsDir,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        await _docker.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        ContainerWaitResponse waitResponse;
        try
        {
            waitResponse = await _docker.Containers.WaitContainerAsync(containerId, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Container {ContainerId} timed out after {Seconds}s", containerId, _options.TimeoutSeconds);
            await StopContainerQuietlyAsync(containerId);
            var logs = await CollectLogsAsync(containerId, CancellationToken.None);
            return new SandboxExecutionResult(
                Succeeded: false,
                Logs: logs,
                FailureReason: $"Timed out after {_options.TimeoutSeconds}s",
                Artifacts: CollectArtifacts(artifactsDir),
                ExitCode: null,
                Duration: DateTimeOffset.UtcNow - started);
        }

        var exitCode = (int)waitResponse.StatusCode;
        var logsOutput = await CollectLogsAsync(containerId, CancellationToken.None);
        var artifacts = CollectArtifacts(artifactsDir);
        var duration = DateTimeOffset.UtcNow - started;

        _logger.LogInformation(
            "Sandbox done step={StepId} exit={ExitCode} duration={Duration}ms artifacts={ArtifactCount}",
            request.StepId, exitCode, duration.TotalMilliseconds, artifacts.Count);

        if (exitCode != 0)
        {
            return new SandboxExecutionResult(
                Succeeded: false,
                Logs: logsOutput,
                FailureReason: $"Container exited with code {exitCode}",
                Artifacts: artifacts,
                ExitCode: exitCode,
                Duration: duration);
        }

        return new SandboxExecutionResult(
            Succeeded: true,
            Logs: logsOutput,
            FailureReason: null,
            Artifacts: artifacts,
            ExitCode: exitCode,
            Duration: duration);
    }

    private async Task<string> CollectLogsAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            var multiplexed = await _docker.Containers.GetContainerLogsAsync(
                containerId,
                tty: false,
                new ContainerLogsParameters { ShowStdout = true, ShowStderr = true },
                cancellationToken);

            using var stdout = new MemoryStream();
            using var stderr = new MemoryStream();
            await multiplexed.CopyOutputToAsync(Stream.Null, stdout, stderr, cancellationToken);

            stdout.Position = 0;
            stderr.Position = 0;
            var stdoutText = await new StreamReader(stdout).ReadToEndAsync(cancellationToken);
            var stderrText = await new StreamReader(stderr).ReadToEndAsync(cancellationToken);

            return string.IsNullOrEmpty(stderrText)
                ? stdoutText
                : $"{stdoutText}\nstderr:\n{stderrText}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect logs for container {ContainerId}", containerId);
            return string.Empty;
        }
    }

    private static IReadOnlyDictionary<string, string> CollectArtifacts(string artifactsDir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(artifactsDir))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(artifactsDir, "*", SearchOption.AllDirectories))
        {
            try
            {
                var relativeName = Path.GetRelativePath(artifactsDir, file);
                result[relativeName] = File.ReadAllText(file);
            }
            catch
            {
                // Skip unreadable files — binary artifacts not surfaced in MVP.
            }
        }

        return result;
    }

    private string ResolveArtifactsDir(string stepId)
    {
        var baseDir = string.IsNullOrWhiteSpace(_options.ArtifactsHostPath)
            ? Path.Combine(Path.GetTempPath(), "autofac-sandboxes")
            : _options.ArtifactsHostPath;

        return Path.Combine(baseDir, stepId);
    }

    private async Task StopContainerQuietlyAsync(string containerId)
    {
        try
        {
            await _docker.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 2 },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not stop container {ContainerId}", containerId);
        }
    }

    private async Task RemoveContainerQuietlyAsync(string containerId)
    {
        try
        {
            await _docker.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove container {ContainerId}", containerId);
        }
    }

    private static SandboxExecutionResult Failure(string reason, DateTimeOffset started) =>
        new(
            Succeeded: false,
            Logs: string.Empty,
            FailureReason: reason,
            Artifacts: new Dictionary<string, string>(),
            ExitCode: null,
            Duration: DateTimeOffset.UtcNow - started);

    public async ValueTask DisposeAsync()
    {
        _docker.Dispose();
        await ValueTask.CompletedTask;
    }
}

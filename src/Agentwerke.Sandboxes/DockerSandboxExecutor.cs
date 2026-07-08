using System.Collections.Concurrent;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentwerke.Sandboxes;

/// <summary>
/// Runs one agent task inside an ephemeral Docker container, captures logs and
/// artifacts from /output, then removes the container.
/// </summary>
public sealed class DockerSandboxExecutor : ISandboxProviderExecutor
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

    public SandboxProviderKind ProviderKind => SandboxProviderKind.Docker;

    public async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken,
        SandboxLogReporter? logReporter = null)
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

        SandboxExecutionResult result;
        try
        {
            result = await RunAndCollectAsync(request, containerId, artifactsDir, started, cancellationToken, logReporter);
        }
        catch
        {
            // Unknown outcome — treat like a failure for retention purposes.
            if (ShouldDeleteOnCompletion(request, succeeded: false))
            {
                await RemoveContainerQuietlyAsync(containerId);
            }

            throw;
        }

        if (ShouldDeleteOnCompletion(request, result.Succeeded))
        {
            await RemoveContainerQuietlyAsync(containerId);
        }

        return result;
    }

    private async Task<string> CreateContainerAsync(
        SandboxExecutionRequest request,
        string image,
        string artifactsDir,
        CancellationToken cancellationToken)
    {
        var env = SandboxRequestDefaults.BuildExecutionEnvironment(request)
            .Concat(request.EnvironmentVariables?.AsEnumerable() ?? Enumerable.Empty<KeyValuePair<string, string>>())
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var command = SandboxRequestDefaults.ResolveCommand(request);
        foreach (var pair in command.EnvironmentVariables?.AsEnumerable() ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            env[pair.Key] = pair.Value;
        }

        var createParams = new CreateContainerParameters
        {
            Image = image,
            Cmd = [.. command.Arguments],
            WorkingDir = command.WorkingDirectory,
            Env = [.. env.Select(static pair => $"{pair.Key}={pair.Value}")],
            HostConfig = new HostConfig
            {
                Memory = (long)_options.MemoryLimitMb * 1024 * 1024,
                CPUQuota = _options.CpuQuota,
                CPUPeriod = 100_000,
                NetworkMode = ResolveNetworkMode(request.Profile?.NetworkPolicy),
                ExtraHosts = ResolveExtraHosts(request.Profile?.NetworkPolicy),
                AutoRemove = false,
                Binds = [$"{artifactsDir}:/output:rw"],
            },
            Labels = new Dictionary<string, string>
            {
                ["agentwerke.run"] = request.RunId,
                ["agentwerke.step"] = request.StepId,
            },
        };

        var response = await _docker.Containers.CreateContainerAsync(createParams, cancellationToken);
        return response.ID;
    }

    private static IList<string>? ResolveExtraHosts(SandboxNetworkPolicy? networkPolicy)
    {
        if (networkPolicy?.Mode == SandboxNetworkAccessMode.None)
        {
            return null;
        }

        // Sandboxed agent runners call LiteLLM through host.docker.internal in the demo/local
        // setup. Plain Docker containers on Linux do not receive that alias automatically, so
        // inject the standard host-gateway mapping just like `docker run --add-host ...`.
        return ["host.docker.internal:host-gateway"];
    }

    private async Task<SandboxExecutionResult> RunAndCollectAsync(
        SandboxExecutionRequest request,
        string containerId,
        string artifactsDir,
        DateTimeOffset started,
        CancellationToken cancellationToken,
        SandboxLogReporter? logReporter)
    {
        await _docker.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);

        var streamedLogs = new ConcurrentQueue<SandboxLogEntry>();
        var logStreamingTask = StreamLogsAsync(
            containerId,
            async (entry, ct) =>
            {
                streamedLogs.Enqueue(entry);
                if (logReporter is not null)
                {
                    await logReporter(entry, ct);
                }
            },
            cancellationToken);

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
            await logStreamingTask;
            var logs = await CollectLogsAsync(containerId, CancellationToken.None);
            var structuredLogs = BuildStructuredLogs(logs, streamedLogs, started);
            return new SandboxExecutionResult(
                Succeeded: false,
                Logs: logs,
                FailureReason: $"Timed out after {_options.TimeoutSeconds}s",
                Artifacts: CollectArtifacts(artifactsDir),
                ExitCode: null,
                Duration: DateTimeOffset.UtcNow - started,
                ProviderSandboxId: containerId,
                CommandState: SandboxCommandState.TimedOut,
                StructuredLogs: structuredLogs,
                ProviderDiagnostics: new Dictionary<string, string>
                {
                    ["provider"] = ProviderKind.ToConfigValue(),
                    ["reason"] = "timeout"
                },
                Endpoints: [],
                Provider: ProviderKind);
        }

        var exitCode = (int)waitResponse.StatusCode;
        await logStreamingTask;
        var logsOutput = await CollectLogsAsync(containerId, CancellationToken.None);
        var artifacts = CollectArtifacts(artifactsDir);
        var duration = DateTimeOffset.UtcNow - started;
        var finalStructuredLogs = BuildStructuredLogs(logsOutput, streamedLogs, started);

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
                Duration: duration,
                ProviderSandboxId: containerId,
                CommandState: SandboxCommandState.Failed,
                StructuredLogs: finalStructuredLogs,
                ProviderDiagnostics: new Dictionary<string, string>
                {
                    ["provider"] = ProviderKind.ToConfigValue()
                },
                Endpoints: [],
                Provider: ProviderKind);
        }

        return new SandboxExecutionResult(
            Succeeded: true,
            Logs: logsOutput,
            FailureReason: null,
            Artifacts: artifacts,
            ExitCode: exitCode,
            Duration: duration,
            ProviderSandboxId: containerId,
            CommandState: SandboxCommandState.Completed,
            StructuredLogs: finalStructuredLogs,
            ProviderDiagnostics: new Dictionary<string, string>
            {
                ["provider"] = ProviderKind.ToConfigValue()
            },
            Endpoints: [],
            Provider: ProviderKind);
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

    private async Task StreamLogsAsync(
        string containerId,
        SandboxLogReporter onLogEntry,
        CancellationToken cancellationToken)
    {
        try
        {
            using var multiplexed = await _docker.Containers.GetContainerLogsAsync(
                containerId,
                tty: false,
                new ContainerLogsParameters
                {
                    Follow = true,
                    ShowStdout = true,
                    ShowStderr = true,
                },
                cancellationToken);

            await using var stdout = new DockerLogRelayStream("stdout", onLogEntry);
            await using var stderr = new DockerLogRelayStream("stderr", onLogEntry);
            await multiplexed.CopyOutputToAsync(Stream.Null, stdout, stderr, cancellationToken);
            await stdout.FlushPendingAsync(cancellationToken);
            await stderr.FlushPendingAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The run itself was cancelled; no additional log forwarding is needed.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Live log streaming failed for Docker container {ContainerId}", containerId);
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

    private static IReadOnlyList<SandboxLogEntry> BuildStructuredLogs(
        string logs,
        ConcurrentQueue<SandboxLogEntry> streamedLogs,
        DateTimeOffset started) =>
        streamedLogs.IsEmpty ? CreateStructuredLogs(logs, started) : [.. streamedLogs];

    private string ResolveArtifactsDir(string stepId)
    {
        var baseDir = string.IsNullOrWhiteSpace(_options.ArtifactsHostPath)
            ? Path.Combine(Path.GetTempPath(), "agentwerke-sandboxes")
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
            Duration: DateTimeOffset.UtcNow - started,
            ProviderSandboxId: null,
            CommandState: SandboxCommandState.Failed,
            StructuredLogs: [],
            ProviderDiagnostics: new Dictionary<string, string>
            {
                ["provider"] = SandboxProviderKind.Docker.ToConfigValue()
            },
            Endpoints: [],
            Provider: SandboxProviderKind.Docker);

    private static IReadOnlyList<SandboxLogEntry> CreateStructuredLogs(string logs, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(logs))
        {
            return [];
        }

        return logs
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => new SandboxLogEntry("combined", line, timestamp))
            .ToArray();
    }

    // Docker's plain bridge network can't enforce the per-host allow-listing that
    // SandboxNetworkPolicy.AllowedHosts implies (that requires an egress proxy/sidecar,
    // which this local fallback provider does not implement) — but a profile asking
    // for Restricted or Open access still needs *some* network, or every sandboxed
    // task that calls out (e.g. agent_sandboxed reaching its model endpoint) fails
    // outright. "None" is the only mode this provider can honor precisely; anything
    // else gets ordinary bridge networking rather than being silently downgraded to
    // no network at all.
    private static string ResolveNetworkMode(SandboxNetworkPolicy? networkPolicy) =>
        networkPolicy is null or { Mode: SandboxNetworkAccessMode.None } ? "none" : "bridge";

    private static bool ShouldDeleteOnCompletion(SandboxExecutionRequest request, bool succeeded)
    {
        var cleanupPolicy = request.Profile?.CleanupPolicy;
        if (!succeeded && cleanupPolicy?.RetainSandboxOnFailure == true)
        {
            return false;
        }

        return cleanupPolicy?.DeleteSandboxOnCompletion ?? true;
    }

    private sealed class DockerLogRelayStream : Stream
    {
        private readonly string _streamName;
        private readonly SandboxLogReporter _reporter;
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _lineBuffer = new();
        private readonly char[] _charBuffer = new char[4096];

        public DockerLogRelayStream(string streamName, SandboxLogReporter reporter)
        {
            _streamName = streamName;
            _reporter = reporter;
        }

        public Task FlushPendingAsync(CancellationToken cancellationToken) =>
            FlushDecoderAsync(cancellationToken);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = buffer;
            while (!remaining.IsEmpty)
            {
                _decoder.Convert(remaining.Span, _charBuffer, flush: false, out var bytesUsed, out var charsUsed, out _);
                remaining = remaining[bytesUsed..];
                if (charsUsed > 0)
                {
                    await AppendTextAsync(new string(_charBuffer, 0, charsUsed), cancellationToken);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FlushPendingAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await FlushPendingAsync(CancellationToken.None);
            await base.DisposeAsync();
        }

        private async Task FlushDecoderAsync(CancellationToken cancellationToken)
        {
            _decoder.Convert(ReadOnlySpan<byte>.Empty, _charBuffer, flush: true, out _, out var charsUsed, out _);
            if (charsUsed > 0)
            {
                await AppendTextAsync(new string(_charBuffer, 0, charsUsed), cancellationToken);
            }

            if (_lineBuffer.Length > 0)
            {
                var trailing = _lineBuffer.ToString();
                _lineBuffer.Clear();
                if (!string.IsNullOrWhiteSpace(trailing))
                {
                    await _reporter(new SandboxLogEntry(_streamName, trailing, DateTimeOffset.UtcNow), cancellationToken);
                }
            }
        }

        private async Task AppendTextAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _lineBuffer.Append(text);
            while (true)
            {
                var current = _lineBuffer.ToString();
                var newlineIndex = current.IndexOf('\n');
                if (newlineIndex < 0)
                {
                    break;
                }

                var line = current[..newlineIndex];
                if (line.EndsWith('\r'))
                {
                    line = line[..^1];
                }

                _lineBuffer.Remove(0, newlineIndex + 1);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    await _reporter(new SandboxLogEntry(_streamName, line, DateTimeOffset.UtcNow), cancellationToken);
                }
            }
        }
    }
}

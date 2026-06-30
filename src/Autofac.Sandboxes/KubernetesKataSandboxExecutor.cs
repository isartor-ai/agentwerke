using System.Diagnostics;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofac.Sandboxes;

/// <summary>
/// Runs a sandbox as a kata-isolated Kubernetes Pod with an egress
/// <see cref="V1NetworkPolicy"/> derived from the run's network policy (#36/#171).
///
/// The manifest translation (<see cref="KubernetesSandboxManifestBuilder"/>) is pure
/// and unit-tested. The live create/wait/logs/teardown path below is compile-verified
/// against the official client and requires a real cluster for end-to-end validation;
/// it degrades to a clear failure when no cluster config is available.
/// </summary>
public sealed class KubernetesKataSandboxExecutor : ISandboxProviderExecutor
{
    private readonly IKubernetesClientProvider _clientProvider;
    private readonly KubernetesKataSandboxProviderOptions _options;
    private readonly ILogger<KubernetesKataSandboxExecutor> _logger;

    public KubernetesKataSandboxExecutor(
        IKubernetesClientProvider clientProvider,
        IOptions<SandboxOptions> options,
        ILogger<KubernetesKataSandboxExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _clientProvider = clientProvider;
        _options = options.Value.KubernetesKata;
        _logger = logger;
    }

    public SandboxProviderKind ProviderKind => SandboxProviderKind.KubernetesKata;

    public async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = _clientProvider.TryCreate(out var configError);
        if (client is null)
        {
            return Failure(
                "Kubernetes is not configured for the 'kubernetes-kata' provider: " +
                $"{configError ?? "no in-cluster or kubeconfig configuration was found"}.");
        }

        var ns = _options.Namespace;
        var pod = KubernetesSandboxManifestBuilder.BuildPod(request, _options);
        var networkPolicy = KubernetesSandboxManifestBuilder.BuildEgressNetworkPolicy(request, _options);
        var podName = pod.Metadata.Name;
        var networkPolicyName = networkPolicy?.Metadata.Name;
        var timeout = TimeSpan.FromSeconds(Math.Max(1, request.Profile?.Resources?.TimeoutSeconds ?? 300));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (networkPolicy is not null)
            {
                await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(
                    networkPolicy, ns, cancellationToken: cancellationToken);
            }

            await client.CoreV1.CreateNamespacedPodAsync(pod, ns, cancellationToken: cancellationToken);

            var (phase, exitCode) = await WaitForCompletionAsync(client, podName, ns, timeout, cancellationToken);
            var logs = await ReadLogsAsync(client, podName, ns, cancellationToken);
            stopwatch.Stop();

            var succeeded = string.Equals(phase, "Succeeded", StringComparison.Ordinal);
            return new SandboxExecutionResult(
                Succeeded: succeeded,
                Logs: logs,
                FailureReason: succeeded
                    ? null
                    : $"Pod ended in phase '{phase}' (exit code {exitCode?.ToString() ?? "unknown"}).",
                Artifacts: new Dictionary<string, string>(),
                ExitCode: exitCode,
                Duration: stopwatch.Elapsed,
                ProviderSandboxId: podName,
                CommandState: succeeded ? SandboxCommandState.Completed : SandboxCommandState.Failed,
                StructuredLogs: [],
                ProviderDiagnostics: new Dictionary<string, string>
                {
                    ["provider"] = ProviderKind.ToConfigValue(),
                    ["namespace"] = ns,
                    ["pod"] = podName,
                    ["phase"] = phase,
                },
                Endpoints: [],
                Provider: ProviderKind);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Kubernetes sandbox run {Pod} failed.", podName);
            return Failure($"Kubernetes sandbox execution failed: {ex.Message}", stopwatch.Elapsed, podName);
        }
        finally
        {
            await TryCleanupAsync(client, podName, networkPolicyName, ns);
        }
    }

    private static async Task<(string Phase, int? ExitCode)> WaitForCompletionAsync(
        IKubernetes client, string podName, string ns, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var pod = await client.CoreV1.ReadNamespacedPodStatusAsync(podName, ns, cancellationToken: ct);
            var phase = pod.Status?.Phase ?? "Unknown";
            if (phase is "Succeeded" or "Failed")
            {
                var exitCode = pod.Status?.ContainerStatuses?
                    .FirstOrDefault()?.State?.Terminated?.ExitCode;
                return (phase, exitCode);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return ("Timeout", null);
    }

    private static async Task<string> ReadLogsAsync(IKubernetes client, string podName, string ns, CancellationToken ct)
    {
        try
        {
            using var stream = await client.CoreV1.ReadNamespacedPodLogAsync(podName, ns, cancellationToken: ct);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private async Task TryCleanupAsync(IKubernetes client, string podName, string? networkPolicyName, string ns)
    {
        try
        {
            await client.CoreV1.DeleteNamespacedPodAsync(podName, ns);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete sandbox pod {Pod}.", podName);
        }

        if (networkPolicyName is not null)
        {
            try
            {
                await client.NetworkingV1.DeleteNamespacedNetworkPolicyAsync(networkPolicyName, ns);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete sandbox network policy {Policy}.", networkPolicyName);
            }
        }
    }

    private SandboxExecutionResult Failure(string reason, TimeSpan? duration = null, string? podName = null) =>
        new(
            Succeeded: false,
            Logs: string.Empty,
            FailureReason: reason,
            Artifacts: new Dictionary<string, string>(),
            ExitCode: null,
            Duration: duration ?? TimeSpan.Zero,
            ProviderSandboxId: podName,
            CommandState: SandboxCommandState.Failed,
            StructuredLogs: [],
            ProviderDiagnostics: new Dictionary<string, string>
            {
                ["provider"] = ProviderKind.ToConfigValue(),
            },
            Endpoints: [],
            Provider: ProviderKind);
}

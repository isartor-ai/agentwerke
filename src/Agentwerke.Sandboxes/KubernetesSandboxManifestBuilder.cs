using System.Text;
using k8s.Models;

namespace Agentwerke.Sandboxes;

/// <summary>
/// Translates an Agentwerke sandbox run into the Kubernetes objects that execute it:
/// a kata-isolated Pod and an egress <see cref="V1NetworkPolicy"/> derived from the
/// run's <see cref="SandboxNetworkPolicy"/>. Pure and deterministic so the pod spec
/// and the network-policy translation are fully unit-testable without a cluster (#171).
/// </summary>
public static class KubernetesSandboxManifestBuilder
{
    public const string ManagedByLabel = "app.kubernetes.io/managed-by";
    public const string RunLabel = "agentwerke.dev/run";
    public const string StepLabel = "agentwerke.dev/step";
    public const string AllowedHostsAnnotation = "agentwerke.dev/allowed-egress-hosts";

    public static string PodName(SandboxExecutionRequest request)
    {
        var raw = $"agentwerke-{Sanitize(request.RunId)}-{Sanitize(request.StepId)}";
        return Truncate(raw.Trim('-'), 63);
    }

    public static V1Pod BuildPod(SandboxExecutionRequest request, KubernetesKataSandboxProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var container = new V1Container
        {
            Name = "agent",
            Image = string.IsNullOrWhiteSpace(request.Image) ? options.DefaultImage : request.Image,
            Resources = BuildResources(request.Profile?.Resources),
        };

        if (request.Command is { Arguments.Count: > 0 } command)
        {
            container.Command = command.Arguments.ToList();
            if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
            {
                container.WorkingDir = command.WorkingDirectory;
            }
        }

        var env = MergeEnv(request.EnvironmentVariables, request.Command?.EnvironmentVariables);
        if (env.Count > 0)
        {
            container.Env = env.Select(kvp => new V1EnvVar { Name = kvp.Key, Value = kvp.Value }).ToList();
        }

        return new V1Pod
        {
            ApiVersion = "v1",
            Kind = "Pod",
            Metadata = new V1ObjectMeta
            {
                Name = PodName(request),
                NamespaceProperty = options.Namespace,
                Labels = Labels(request),
            },
            Spec = new V1PodSpec
            {
                // kata isolation; null/empty means use the cluster default runtime.
                RuntimeClassName = string.IsNullOrWhiteSpace(options.RuntimeClassName) ? null : options.RuntimeClassName,
                RestartPolicy = "Never",
                AutomountServiceAccountToken = false,
                Containers = [container],
            },
        };
    }

    /// <summary>
    /// Translates the run's network policy into an egress <see cref="V1NetworkPolicy"/>:
    /// <list type="bullet">
    /// <item><c>Open</c> → <c>null</c> (no policy; cluster-default egress applies).</item>
    /// <item><c>None</c> → an empty-egress policy (deny all egress).</item>
    /// <item><c>Restricted</c> → allow DNS only; allowed hosts are recorded as an
    /// annotation for an FQDN-aware CNI (e.g. Cilium) to enforce per-host.</item>
    /// </list>
    /// </summary>
    public static V1NetworkPolicy? BuildEgressNetworkPolicy(
        SandboxExecutionRequest request,
        KubernetesKataSandboxProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var mode = request.Profile?.NetworkPolicy?.Mode ?? SandboxNetworkAccessMode.None;
        if (mode == SandboxNetworkAccessMode.Open)
        {
            return null;
        }

        var metadata = new V1ObjectMeta
        {
            Name = Truncate($"{PodName(request)}-egress", 63),
            NamespaceProperty = options.Namespace,
            Labels = Labels(request),
        };

        var egress = new List<V1NetworkPolicyEgressRule>();
        if (mode == SandboxNetworkAccessMode.Restricted)
        {
            // Always permit DNS so name resolution works; per-host allow-listing is
            // surfaced as an annotation for an FQDN-aware CNI to enforce.
            egress.Add(new V1NetworkPolicyEgressRule
            {
                Ports =
                [
                    new V1NetworkPolicyPort { Protocol = "UDP", Port = "53" },
                    new V1NetworkPolicyPort { Protocol = "TCP", Port = "53" },
                ],
            });

            var allowedHosts = request.Profile?.NetworkPolicy?.AllowedHosts;
            if (allowedHosts is { Count: > 0 })
            {
                metadata.Annotations = new Dictionary<string, string>
                {
                    [AllowedHostsAnnotation] = string.Join(",", allowedHosts),
                };
            }
        }

        // mode == None leaves egress empty → deny all egress.
        return new V1NetworkPolicy
        {
            ApiVersion = "networking.k8s.io/v1",
            Kind = "NetworkPolicy",
            Metadata = metadata,
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector
                {
                    MatchLabels = new Dictionary<string, string>
                    {
                        [RunLabel] = Sanitize(request.RunId),
                        [StepLabel] = Sanitize(request.StepId),
                    },
                },
                PolicyTypes = ["Egress"],
                Egress = egress,
            },
        };
    }

    private static Dictionary<string, string> Labels(SandboxExecutionRequest request) => new()
    {
        [ManagedByLabel] = "agentwerke",
        [RunLabel] = Sanitize(request.RunId),
        [StepLabel] = Sanitize(request.StepId),
    };

    private static Dictionary<string, string> MergeEnv(
        IReadOnlyDictionary<string, string>? primary,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (primary is not null)
        {
            foreach (var kvp in primary)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        if (overrides is not null)
        {
            foreach (var kvp in overrides)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        return merged;
    }

    private static V1ResourceRequirements? BuildResources(SandboxResourceLimits? limits)
    {
        if (limits is null)
        {
            return null;
        }

        var values = new Dictionary<string, ResourceQuantity>();
        if (limits.MemoryMb is { } memoryMb and > 0)
        {
            values["memory"] = new ResourceQuantity($"{memoryMb}Mi");
        }

        if (limits.CpuMilliCores is { } cpu and > 0)
        {
            values["cpu"] = new ResourceQuantity($"{cpu}m");
        }

        return values.Count == 0 ? null : new V1ResourceRequirements { Limits = values };
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(sanitized) ? "x" : sanitized;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd('-');
}

using k8s;

namespace Autofac.Sandboxes;

/// <summary>
/// Lazily builds a Kubernetes client from the ambient config (in-cluster service
/// account or kubeconfig). Returns <c>null</c> with a diagnostic message when no
/// cluster config is available, so the sandbox executor degrades gracefully rather
/// than failing dependency injection on hosts that never use the K8s provider.
/// </summary>
public interface IKubernetesClientProvider
{
    IKubernetes? TryCreate(out string? error);
}

public sealed class DefaultKubernetesClientProvider : IKubernetesClientProvider
{
    private readonly object _gate = new();
    private IKubernetes? _client;
    private string? _error;
    private bool _initialized;

    public IKubernetes? TryCreate(out string? error)
    {
        lock (_gate)
        {
            if (!_initialized)
            {
                try
                {
                    var config = KubernetesClientConfiguration.BuildDefaultConfig();
                    _client = new Kubernetes(config);
                }
                catch (Exception ex)
                {
                    _error = ex.Message;
                }

                _initialized = true;
            }

            error = _error;
            return _client;
        }
    }
}

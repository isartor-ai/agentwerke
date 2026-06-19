namespace Autofac.Sandboxes;

public enum SandboxProviderKind
{
    Docker,
    OpenSandbox,
    KubernetesKata
}

public static class SandboxProviderNames
{
    public const string Docker = "docker";
    public const string OpenSandbox = "opensandbox";
    public const string KubernetesKata = "kubernetes-kata";

    public static SandboxProviderKind Parse(string? provider)
    {
        var normalized = provider?.Trim();

        return normalized?.ToLowerInvariant() switch
        {
            null or "" => SandboxProviderKind.Docker,
            Docker => SandboxProviderKind.Docker,
            OpenSandbox => SandboxProviderKind.OpenSandbox,
            KubernetesKata or "kuberneteskata" => SandboxProviderKind.KubernetesKata,
            _ => throw new InvalidOperationException(
                $"Unsupported sandbox provider '{provider}'. " +
                $"Set '{SandboxOptions.Section}:Provider' to one of: {Docker}, {OpenSandbox}, {KubernetesKata}.")
        };
    }

    public static string ToConfigValue(this SandboxProviderKind kind) =>
        kind switch
        {
            SandboxProviderKind.Docker => Docker,
            SandboxProviderKind.OpenSandbox => OpenSandbox,
            SandboxProviderKind.KubernetesKata => KubernetesKata,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported sandbox provider kind.")
        };
}

namespace Agentwerke.Sandboxes.Tests;

public sealed class KubernetesSandboxManifestBuilderTests
{
    private static readonly KubernetesKataSandboxProviderOptions Options = new();

    private static SandboxExecutionRequest Request(
        string runId = "run-1",
        string stepId = "step-1",
        string? image = null,
        SandboxExecutionProfile? profile = null) =>
        new(
            RunId: runId,
            StepId: stepId,
            AgentName: "agent",
            Action: "action",
            Environment: "staging",
            PurposeType: "general",
            PolicyTag: "tag",
            Attempt: 1,
            Image: image,
            Profile: profile);

    [Fact]
    public void BuildPod_SetsKataRuntimeNamespaceLabelsAndDefaultImage()
    {
        var pod = KubernetesSandboxManifestBuilder.BuildPod(Request(), Options);

        Assert.Equal("kata", pod.Spec.RuntimeClassName);
        Assert.Equal("default", pod.Metadata.NamespaceProperty);
        Assert.Equal("Never", pod.Spec.RestartPolicy);
        Assert.False(pod.Spec.AutomountServiceAccountToken);
        Assert.Equal("autofac", pod.Metadata.Labels[KubernetesSandboxManifestBuilder.ManagedByLabel]);
        Assert.Equal("run-1", pod.Metadata.Labels[KubernetesSandboxManifestBuilder.RunLabel]);
        Assert.Equal("step-1", pod.Metadata.Labels[KubernetesSandboxManifestBuilder.StepLabel]);
        var container = Assert.Single(pod.Spec.Containers);
        Assert.Equal("alpine:3.19", container.Image);
    }

    [Fact]
    public void BuildPod_AppliesResourceLimits()
    {
        var profile = new SandboxExecutionProfile(
            Resources: new SandboxResourceLimits(CpuMilliCores: 500, MemoryMb: 256));

        var pod = KubernetesSandboxManifestBuilder.BuildPod(Request(profile: profile), Options);

        var limits = Assert.Single(pod.Spec.Containers).Resources.Limits;
        Assert.Equal("256Mi", limits["memory"].ToString());
        Assert.Equal("500m", limits["cpu"].ToString());
    }

    [Fact]
    public void BuildPod_UsesRequestImageWhenProvided()
    {
        var pod = KubernetesSandboxManifestBuilder.BuildPod(Request(image: "ghcr.io/x:1"), Options);
        Assert.Equal("ghcr.io/x:1", Assert.Single(pod.Spec.Containers).Image);
    }

    [Fact]
    public void BuildPod_SanitizesAndBoundsName()
    {
        var pod = KubernetesSandboxManifestBuilder.BuildPod(Request(runId: "Run_ABC#1", stepId: "Step/2"), Options);

        Assert.Matches("^[a-z0-9-]+$", pod.Metadata.Name);
        Assert.True(pod.Metadata.Name.Length <= 63);
    }

    [Fact]
    public void BuildEgress_OpenMode_ReturnsNull()
    {
        var profile = new SandboxExecutionProfile(
            NetworkPolicy: new SandboxNetworkPolicy(SandboxNetworkAccessMode.Open));

        Assert.Null(KubernetesSandboxManifestBuilder.BuildEgressNetworkPolicy(Request(profile: profile), Options));
    }

    [Fact]
    public void BuildEgress_NoneMode_DeniesAllEgress()
    {
        // No network policy on the profile → defaults to None.
        var np = KubernetesSandboxManifestBuilder.BuildEgressNetworkPolicy(Request(), Options);

        Assert.NotNull(np);
        Assert.Equal("Egress", Assert.Single(np!.Spec.PolicyTypes));
        Assert.Empty(np.Spec.Egress);
    }

    [Fact]
    public void BuildEgress_RestrictedMode_AllowsDnsAndAnnotatesAllowedHosts()
    {
        var profile = new SandboxExecutionProfile(
            NetworkPolicy: new SandboxNetworkPolicy(
                SandboxNetworkAccessMode.Restricted,
                ["api.github.com", "registry.npmjs.org"]));

        var np = KubernetesSandboxManifestBuilder.BuildEgressNetworkPolicy(Request(profile: profile), Options);

        Assert.NotNull(np);
        var rule = Assert.Single(np!.Spec.Egress);
        Assert.Equal(2, rule.Ports.Count);
        Assert.All(rule.Ports, port => Assert.Equal("53", port.Port.Value));
        Assert.Equal(
            "api.github.com,registry.npmjs.org",
            np.Metadata.Annotations[KubernetesSandboxManifestBuilder.AllowedHostsAnnotation]);
    }
}

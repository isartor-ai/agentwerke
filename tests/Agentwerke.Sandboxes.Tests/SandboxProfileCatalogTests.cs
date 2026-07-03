using Agentwerke.Sandboxes;

namespace Agentwerke.Sandboxes.Tests;

public sealed class SandboxProfileCatalogTests
{
    [Theory]
    [InlineData(SandboxProfileNames.Offline)]
    [InlineData(SandboxProfileNames.RepoRead)]
    [InlineData(SandboxProfileNames.RepoWrite)]
    [InlineData(SandboxProfileNames.Deployment)]
    public void TryGet_KnownProfile_ReturnsDefinition(string name)
    {
        var found = SandboxProfileCatalog.TryGet(name, out var definition);

        Assert.True(found);
        Assert.NotNull(definition);
        Assert.Equal(name, definition!.Name);
        Assert.False(string.IsNullOrWhiteSpace(definition.Description));
    }

    [Fact]
    public void TryGet_UnknownProfile_ReturnsFalse()
    {
        var found = SandboxProfileCatalog.TryGet("not-a-profile", out var definition);

        Assert.False(found);
        Assert.Null(definition);
    }

    [Fact]
    public void Default_IsOffline()
    {
        Assert.Equal(SandboxProfileNames.Offline, SandboxProfileCatalog.Default);
    }

    [Fact]
    public void Offline_HasNoNetworkAccessNoMountsNoCredentials()
    {
        SandboxProfileCatalog.TryGet(SandboxProfileNames.Offline, out var definition);

        Assert.Equal(SandboxNetworkAccessMode.None, definition!.Profile.NetworkPolicy!.Mode);
        Assert.Empty(definition.Profile.FilesystemMounts!);
        Assert.Empty(definition.Profile.CredentialBindings!);
        Assert.Empty(definition.RequiredCredentials);
    }

    [Fact]
    public void RepoRead_MountsReadOnlyWorkspaceAndBindsReadOnlyGitHubToken()
    {
        SandboxProfileCatalog.TryGet(SandboxProfileNames.RepoRead, out var definition);

        var mount = Assert.Single(definition!.Profile.FilesystemMounts!);
        Assert.True(mount.ReadOnly);

        var binding = Assert.Single(definition.Profile.CredentialBindings!);
        Assert.Equal("github-token", binding.Name);
        Assert.Equal(SandboxCredentialBindingMode.File, binding.Mode);
        Assert.True(binding.ReadOnly);
        Assert.Equal(SandboxNetworkAccessMode.Restricted, definition.Profile.NetworkPolicy!.Mode);
        Assert.Contains("github.com", definition.Profile.NetworkPolicy.AllowedHosts!);
        Assert.Contains("github-token", definition.RequiredCredentials);
    }

    [Fact]
    public void RepoWrite_MountsWritableWorkspaceAndRetainsSandboxOnFailure()
    {
        SandboxProfileCatalog.TryGet(SandboxProfileNames.RepoWrite, out var definition);

        var mount = Assert.Single(definition!.Profile.FilesystemMounts!);
        Assert.False(mount.ReadOnly);
        Assert.True(definition.Profile.CleanupPolicy!.RetainSandboxOnFailure);
    }

    [Fact]
    public void Deployment_HasNoSourceMountAndBindsDeploymentCredential()
    {
        SandboxProfileCatalog.TryGet(SandboxProfileNames.Deployment, out var definition);

        Assert.Empty(definition!.Profile.FilesystemMounts!);
        var binding = Assert.Single(definition.Profile.CredentialBindings!);
        Assert.Equal("deployment-credentials", binding.Name);
        Assert.Equal(SandboxCredentialBindingMode.File, binding.Mode);
        Assert.Contains("deployment-credentials", definition.RequiredCredentials);
    }

    [Fact]
    public void Resolve_NamespacesWorkspaceVolumeByRunId()
    {
        var profile = SandboxProfileCatalog.Resolve(SandboxProfileNames.RepoWrite, "run-42");

        var mount = Assert.Single(profile.FilesystemMounts!);
        Assert.Equal("agentwerke-run-run-42-workspace", mount.Source);
    }

    [Fact]
    public void Resolve_SameProfile_DifferentRuns_ProducesDistinctVolumes()
    {
        var profileA = SandboxProfileCatalog.Resolve(SandboxProfileNames.RepoRead, "run-a");
        var profileB = SandboxProfileCatalog.Resolve(SandboxProfileNames.RepoRead, "run-b");

        Assert.NotEqual(
            profileA.FilesystemMounts!.Single().Source,
            profileB.FilesystemMounts!.Single().Source);
    }

    [Fact]
    public void Resolve_IsDeterministic_ForSameProfileAndRun()
    {
        var first = SandboxProfileCatalog.Resolve(SandboxProfileNames.RepoWrite, "run-1");
        var second = SandboxProfileCatalog.Resolve(SandboxProfileNames.RepoWrite, "run-1");

        Assert.Equal(first.FilesystemMounts!.Single().Source, second.FilesystemMounts!.Single().Source);
        Assert.Equal(first.NetworkPolicy!.Mode, second.NetworkPolicy!.Mode);
        Assert.Equal(first.Resources!.MemoryMb, second.Resources!.MemoryMb);
    }

    [Fact]
    public void Resolve_UnknownProfile_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => SandboxProfileCatalog.Resolve("not-a-profile", "run-1"));
    }

    [Fact]
    public void Names_ContainsAllFourProfiles()
    {
        Assert.Equal(4, SandboxProfileCatalog.Names.Count);
        Assert.Equal(4, SandboxProfileCatalog.All.Count);
    }
}

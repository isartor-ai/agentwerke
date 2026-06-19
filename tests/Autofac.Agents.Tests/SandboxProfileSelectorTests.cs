using Autofac.AgentSecOps;

namespace Autofac.Agents.Tests;

public sealed class SandboxProfileSelectorTests
{
    private readonly SandboxProfileSelector _selector = new();

    [Fact]
    public void Select_AgentDeclaresNoProfiles_DefaultsToOfflineAndAllows()
    {
        var result = _selector.Select(new SandboxProfileSelectionRequest(
            AgentName: "business-analyst",
            Action: "requirement-design",
            RequestedProfile: null,
            AgentAllowedProfiles: [],
            Environment: null,
            PolicyTag: "doc-generation",
            PurposeType: "requirement-design",
            RiskLevel: "low"));

        Assert.True(result.Allowed);
        Assert.Equal("offline", result.SelectedProfile);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Select_RequestedProfileInAgentAllowList_Allows()
    {
        var result = _selector.Select(new SandboxProfileSelectionRequest(
            AgentName: "implementation-engineer",
            Action: "implement",
            RequestedProfile: "repo-write",
            AgentAllowedProfiles: ["repo-write"],
            Environment: "sandbox",
            PolicyTag: "implementation",
            PurposeType: "implementation",
            RiskLevel: "medium"));

        Assert.True(result.Allowed);
        Assert.Equal("repo-write", result.SelectedProfile);
    }

    [Fact]
    public void Select_RequestedProfileNotInAgentAllowList_Rejects()
    {
        var result = _selector.Select(new SandboxProfileSelectionRequest(
            AgentName: "senior-code-reviewer",
            Action: "review-code",
            RequestedProfile: "repo-write",
            AgentAllowedProfiles: ["repo-read"],
            Environment: "sandbox",
            PolicyTag: "code-review",
            PurposeType: "code-review",
            RiskLevel: "medium"));

        Assert.False(result.Allowed);
        Assert.Null(result.SelectedProfile);
        Assert.Contains("not authorized", result.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_DeploymentProfile_UnderCriticalRisk_RejectsEvenIfAgentAllowsIt()
    {
        var result = _selector.Select(new SandboxProfileSelectionRequest(
            AgentName: "deploy-agent",
            Action: "deploy",
            RequestedProfile: "deployment",
            AgentAllowedProfiles: ["deployment"],
            Environment: "production",
            PolicyTag: "deploy-production",
            PurposeType: "deployment",
            RiskLevel: "critical"));

        Assert.False(result.Allowed);
        Assert.Contains("not permitted for critical-risk actions", result.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_DeploymentProfile_UnderNonCriticalRisk_Allows()
    {
        var result = _selector.Select(new SandboxProfileSelectionRequest(
            AgentName: "deploy-agent",
            Action: "deploy",
            RequestedProfile: "deployment",
            AgentAllowedProfiles: ["deployment"],
            Environment: "staging",
            PolicyTag: "deploy-staging",
            PurposeType: "deployment",
            RiskLevel: "high"));

        Assert.True(result.Allowed);
        Assert.Equal("deployment", result.SelectedProfile);
    }

    [Fact]
    public void Select_BlankRequestedProfile_DefaultsToOffline()
    {
        var result = _selector.Select(new SandboxProfileSelectionRequest(
            AgentName: "test-agent",
            Action: "run-tests",
            RequestedProfile: "   ",
            AgentAllowedProfiles: ["repo-read"],
            Environment: null,
            PolicyTag: "test-gate",
            PurposeType: "quality",
            RiskLevel: "low"));

        Assert.False(result.Allowed);
        Assert.Equal("offline", result.RequestedProfile);
    }
}

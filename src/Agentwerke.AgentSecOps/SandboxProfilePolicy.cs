namespace Agentwerke.AgentSecOps;

/// <summary>
/// Asks whether an agent may use a named sandbox profile for a given action. This is a
/// separate, additional gate from <see cref="IPolicyEvaluationService"/>: the action-level
/// policy decision (allow/deny/escalate) must already be "allow" before this runs, and this
/// decides the *capability* (network egress, repository write, credentials) the sandbox
/// receives, not whether the action itself is permitted.
///
/// Deliberately has no dependency on Agentwerke.Sandboxes: it reasons about profile *names*
/// (declared on the agent profile / requested by the workflow task), not the technical
/// resource/network/volume mapping, which lives in Agentwerke.Sandboxes.SandboxProfileCatalog.
/// </summary>
public sealed record SandboxProfileSelectionRequest(
    string AgentName,
    string Action,
    string? RequestedProfile,
    IReadOnlyList<string> AgentAllowedProfiles,
    string? Environment,
    string PolicyTag,
    string PurposeType,
    string RiskLevel);

public sealed record SandboxProfileSelectionResult(
    bool Allowed,
    string RequestedProfile,
    string? SelectedProfile,
    string Rationale,
    IReadOnlyList<string> Diagnostics)
{
    public static SandboxProfileSelectionResult Allow(
        string requestedProfile, string rationale, IReadOnlyList<string> diagnostics) =>
        new(true, requestedProfile, requestedProfile, rationale, diagnostics);

    public static SandboxProfileSelectionResult Reject(
        string requestedProfile, string rationale, IReadOnlyList<string> diagnostics) =>
        new(false, requestedProfile, null, rationale, diagnostics);
}

public interface ISandboxProfileSelector
{
    SandboxProfileSelectionResult Select(SandboxProfileSelectionRequest request);
}

/// <summary>
/// Default-deny sandbox profile selector. An agent with no declared allowed profiles may
/// only use the most restrictive ("offline") profile. Deployment-capable profiles are
/// additionally blocked for critical-risk actions regardless of the agent's declared
/// allow-list, as defense in depth alongside the action-level policy decision.
/// </summary>
public sealed class SandboxProfileSelector : ISandboxProfileSelector
{
    private const string OfflineProfile = "offline";
    private const string DeploymentProfile = "deployment";
    private const string CriticalRisk = "critical";

    public SandboxProfileSelectionResult Select(SandboxProfileSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<string>();
        var requested = string.IsNullOrWhiteSpace(request.RequestedProfile)
            ? OfflineProfile
            : request.RequestedProfile.Trim();
        diagnostics.Add($"Requested sandbox profile '{requested}' for agent '{request.AgentName}', action '{request.Action}'.");

        var allowed = request.AgentAllowedProfiles.Count > 0
            ? request.AgentAllowedProfiles
            : [OfflineProfile];
        diagnostics.Add($"Agent '{request.AgentName}' declares allowed sandbox profiles: {string.Join(", ", allowed)}.");

        if (!allowed.Contains(requested, StringComparer.OrdinalIgnoreCase))
        {
            var rationale = $"Agent '{request.AgentName}' is not authorized to request sandbox profile '{requested}'. " +
                $"Allowed profiles: {string.Join(", ", allowed)}.";
            diagnostics.Add(rationale);
            return SandboxProfileSelectionResult.Reject(requested, rationale, diagnostics);
        }

        if (string.Equals(requested, DeploymentProfile, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.RiskLevel, CriticalRisk, StringComparison.OrdinalIgnoreCase))
        {
            var rationale = $"Sandbox profile '{DeploymentProfile}' is not permitted for critical-risk actions " +
                $"(policy tag '{request.PolicyTag}'). Require explicit human approval and a lower-risk path first.";
            diagnostics.Add(rationale);
            return SandboxProfileSelectionResult.Reject(requested, rationale, diagnostics);
        }

        var allowRationale = $"Sandbox profile '{requested}' is authorized for agent '{request.AgentName}'.";
        diagnostics.Add(allowRationale);
        return SandboxProfileSelectionResult.Allow(requested, allowRationale, diagnostics);
    }
}

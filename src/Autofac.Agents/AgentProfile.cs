namespace Autofac.Agents;

public sealed class AgentProfile
{
    public string AgentId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public IReadOnlyList<AgentSkillRef> Skills { get; init; } = [];
    public IReadOnlyList<string> SupportedEnvironments { get; init; } = [];
    public IReadOnlyList<string> SupportedPolicyTags { get; init; } = [];
}

/// <summary>
/// In-process catalog of known agent profiles. Real registrations will come from
/// the agent runtime in a later milestone; this is the MVP stub.
/// </summary>
public static class AgentRegistry
{
    private static readonly Dictionary<string, AgentProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["deploy-agent"] = new AgentProfile
        {
            AgentId = "deploy-agent",
            Name = "Deploy Agent",
            Description = "Handles code deployment, rollback, and environment promotion tasks.",
            Category = "devops",
            Skills =
            [
                new AgentSkillRef("deploy", "Deploy", "Deploy artefacts to an environment",
                    ["deploy", "rollback", "promote"], SkillManifestId: "shipping-and-launch"),
                new AgentSkillRef("health-check", "Health Check", "Verify deployment health",
                    ["health-check", "verify"])
            ],
            SupportedEnvironments = ["staging", "production", "dev"],
            SupportedPolicyTags = ["deploy-staging", "deploy-production", "deploy-rollback"]
        },
        ["security-agent"] = new AgentProfile
        {
            AgentId = "security-agent",
            Name = "Security Agent",
            Description = "Performs security scans, secret rotation, and compliance checks.",
            Category = "security",
            Skills =
            [
                new AgentSkillRef("secret-rotation", "Secret Rotation", "Rotate credentials and secrets",
                    ["rotate-secrets", "revoke-credentials"], SkillManifestId: "security-and-hardening"),
                new AgentSkillRef("vuln-scan", "Vulnerability Scan", "Run SAST/DAST and dependency scans",
                    ["scan", "audit"], SkillManifestId: "security-and-hardening")
            ],
            SupportedEnvironments = ["all"],
            SupportedPolicyTags = ["secret-rotation", "security-scan", "compliance-check"]
        },
        ["infra-agent"] = new AgentProfile
        {
            AgentId = "infra-agent",
            Name = "Infrastructure Agent",
            Description = "Provisions and configures cloud infrastructure resources.",
            Category = "infrastructure",
            Skills =
            [
                new AgentSkillRef("provision", "Provision", "Create or update infrastructure resources",
                    ["provision", "configure", "teardown"], SkillManifestId: "incremental-implementation"),
                new AgentSkillRef("scale", "Scale", "Scale compute resources up or down",
                    ["scale-up", "scale-down"])
            ],
            SupportedEnvironments = ["aws", "gcp", "azure"],
            SupportedPolicyTags = ["infra-change", "scale-event", "resource-provision"]
        },
        ["test-agent"] = new AgentProfile
        {
            AgentId = "test-agent",
            Name = "Test Agent",
            Description = "Runs automated test suites and reports results.",
            Category = "quality",
            Skills =
            [
                new AgentSkillRef("run-tests", "Run Tests", "Execute test suites and collect results",
                    ["run-tests", "run-integration-tests", "run-e2e"], SkillManifestId: "test-driven-development"),
                new AgentSkillRef("coverage", "Coverage", "Measure and report code coverage",
                    ["coverage-report"], SkillManifestId: "test-driven-development")
            ],
            SupportedEnvironments = ["all"],
            SupportedPolicyTags = ["test-gate", "quality-check"]
        },
        ["github-agent"] = new AgentProfile
        {
            AgentId = "github-agent",
            Name = "GitHub Agent",
            Description = "Creates branches and pull requests in the configured GitHub repository.",
            Category = "integration",
            Skills =
            [
                new AgentSkillRef(
                    "github-branching",
                    "GitHub Branching",
                    "Create deterministic Autofac branches for workflow runs",
                    ["github.create_branch"],
                    SkillManifestId: "git-workflow-and-versioning"),
                new AgentSkillRef(
                    "github-pr",
                    "GitHub Pull Request",
                    "Open draft pull requests with Autofac run evidence",
                    ["github.create_pull_request", "github.create_pr"],
                    SkillManifestId: "git-workflow-and-versioning")
            ],
            SupportedEnvironments = ["github"],
            SupportedPolicyTags = ["repo-change", "pull-request", "branch-create"]
        }
    };

    public static AgentProfile? Find(string agentName) =>
        Profiles.TryGetValue(agentName, out var profile) ? profile : null;

    public static IReadOnlyList<AgentProfile> All() =>
        Profiles.Values.ToList().AsReadOnly();
}

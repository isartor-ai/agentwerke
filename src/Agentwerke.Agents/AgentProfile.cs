namespace Agentwerke.Agents;

public sealed class AgentProfile
{
    public string AgentId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public IReadOnlyList<AgentSkillRef> Skills { get; init; } = [];
    public IReadOnlyList<string> SupportedEnvironments { get; init; } = [];
    public IReadOnlyList<string> SupportedPolicyTags { get; init; } = [];

    // ── File-registry fields (AGENT.md). Built-in profiles leave these at defaults. ──

    /// <summary>Execution engine: "agent-model" (in-process) or "claude-code" (sandbox).</summary>
    public string Runner { get; init; } = "agent-model";

    /// <summary>Optional model id override.</summary>
    public string? Model { get; init; }

    /// <summary>Sandbox base image (used by the claude-code runner).</summary>
    public string? DockerImage { get; init; }

    /// <summary>Sandbox network policy: "none" (default) or "bridge".</summary>
    public string Network { get; init; } = "none";

    /// <summary>Tool allow-list (gateway tools and/or sandbox --allowedTools).</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>Tool deny-list.</summary>
    public IReadOnlyList<string> DeniedTools { get; init; } = [];

    /// <summary>Secret names resolved at launch (never inlined).</summary>
    public IReadOnlyList<string> Secrets { get; init; } = [];

    /// <summary>Actions this agent answers to (for action→agent resolution).</summary>
    public IReadOnlyList<string> SupportedActions { get; init; } = [];

    /// <summary>
    /// Named sandbox profiles (see Agentwerke.Sandboxes.SandboxProfileCatalog) this agent may
    /// request. Empty means the agent may only use the "offline" profile (least privilege).
    /// </summary>
    public IReadOnlyList<string> SandboxProfiles { get; init; } = [];

    /// <summary>System prompt / standing instructions (the AGENT.md body).</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>SHA-256 of the source AGENT.md, for audit. Null for built-ins.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Origin of the profile: "builtin" or "file".</summary>
    public string Source { get; init; } = "builtin";
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
            SupportedPolicyTags = ["deploy-staging", "deploy-production", "deploy-rollback"],
            SandboxProfiles = ["deployment"]
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
            SupportedPolicyTags = ["secret-rotation", "security-scan", "compliance-check"],
            SandboxProfiles = ["repo-read"]
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
            SupportedPolicyTags = ["infra-change", "scale-event", "resource-provision"],
            SandboxProfiles = ["deployment"]
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
            SupportedPolicyTags = ["test-gate", "quality-check"],
            SandboxProfiles = ["repo-read"]
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
                    "Create deterministic Agentwerke branches for workflow runs",
                    ["github.create_branch"],
                    SkillManifestId: "git-workflow-and-versioning"),
                new AgentSkillRef(
                    "github-pr",
                    "GitHub Pull Request",
                    "Open draft pull requests with Agentwerke run evidence",
                    ["github.create_pull_request", "github.create_pr"],
                    SkillManifestId: "git-workflow-and-versioning")
            ],
            SupportedEnvironments = ["github"],
            SupportedPolicyTags = ["repo-change", "pull-request", "branch-create"],
            SandboxProfiles = ["repo-write"]
        },

        // ── SDLC agents (autonomous software-delivery workflow) ──────────────────
        // Skill manifests are intentionally left unbound (SkillManifestId omitted) so
        // these profiles run in stacks without a populated skills directory. Binding
        // real manifests is a later step. See issue isartor-ai/agentwerke-private#89.
        ["business-analyst"] = new AgentProfile
        {
            AgentId = "business-analyst",
            Name = "Business Analyst",
            Description = "Turns an idea or GitHub issue into a clear requirements specification (Markdown).",
            Category = "analysis",
            Skills =
            [
                new AgentSkillRef("requirement-design", "Requirement Design",
                    "Elicit and structure requirements from an idea or issue into a spec",
                    ["requirement-design", "design-requirements"])
            ],
            SupportedEnvironments = ["all"],
            SupportedPolicyTags = ["requirement-design", "doc-generation"]
        },
        ["solution-architect"] = new AgentProfile
        {
            AgentId = "solution-architect",
            Name = "Solution Architect",
            Description = "Evaluates requirements and produces a technical design specification (Markdown).",
            Category = "architecture",
            Skills =
            [
                new AgentSkillRef("architecture-design", "Architecture Design",
                    "Produce a technical design / architecture specification from requirements",
                    ["architecture-design", "design-architecture"])
            ],
            SupportedEnvironments = ["all"],
            SupportedPolicyTags = ["architecture-design", "doc-generation"]
        },
        ["technical-analyst"] = new AgentProfile
        {
            AgentId = "technical-analyst",
            Name = "Technical Analyst",
            Description = "Produces a TDD-driven implementation plan from requirements and architecture.",
            Category = "planning",
            Skills =
            [
                new AgentSkillRef("implementation-plan", "Implementation Plan",
                    "Break work into a TDD-driven implementation plan with acceptance criteria",
                    ["technical-analysis", "implementation-plan"])
            ],
            SupportedEnvironments = ["all"],
            SupportedPolicyTags = ["implementation-plan", "doc-generation"]
        },
        ["implementation-engineer"] = new AgentProfile
        {
            AgentId = "implementation-engineer",
            Name = "Implementation Engineer",
            Description = "Implements tasks from the plan in a sandbox and opens a pull request for review.",
            Category = "engineering",
            Skills =
            [
                new AgentSkillRef("implement", "Implement",
                    "Implement plan tasks with tests and open a pull request",
                    ["implement", "github.create_pull_request", "github.create_pr"])
            ],
            SupportedEnvironments = ["sandbox", "ci", "github"],
            SupportedPolicyTags = ["implementation", "pull-request", "repo-change"],
            SandboxProfiles = ["repo-write"]
        },
        ["senior-code-reviewer"] = new AgentProfile
        {
            AgentId = "senior-code-reviewer",
            Name = "Senior Code Reviewer",
            Description = "Reviews a pull request, leaves feedback, and proposes changes.",
            Category = "quality",
            Skills =
            [
                new AgentSkillRef("code-review", "Code Review",
                    "Review a pull request for correctness and quality, and propose changes",
                    ["review-code", "code-review"])
            ],
            SupportedEnvironments = ["sandbox", "github"],
            SupportedPolicyTags = ["code-review", "quality-check"],
            SandboxProfiles = ["repo-read"]
        }
    };

    public static AgentProfile? Find(string agentName) =>
        Profiles.TryGetValue(agentName, out var profile) ? profile : null;

    public static IReadOnlyList<AgentProfile> All() =>
        Profiles.Values.ToList().AsReadOnly();
}

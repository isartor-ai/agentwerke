using Agentwerke.Agents.Prompts;
using Agentwerke.Agents.Skills;
using Agentwerke.Domain.AgentRuntime;

namespace Agentwerke.Agents.Tests;

public sealed class AgentPromptAssemblerTests
{
    [Fact]
    public void Assemble_WithInlinePrompt_InterpolatesVariablesAndPreservesSections()
    {
        var assembler = new AgentPromptAssembler();

        var result = assembler.Assemble(new AgentPromptAssemblyRequest(
            RunId: "run-123",
            StepId: "step-456",
            NodeId: "Deploy",
            NodeName: "Deploy Service",
            AgentName: "deploy-agent",
            AgentDescription: "Handles deployment tasks.",
            AgentCategory: "devops",
            Action: "cloud.deploy_artifact",
            Environment: "staging",
            PurposeType: "release",
            PolicyTag: "deploy-staging",
            Attempt: 2,
            RequiresEvidence: ["artifact_signed"],
            Prompt: new AgentPromptContract
            {
                Inline = "Deploy {{workflow_name}} to {{environment}} on attempt {{attempt}}.",
                Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["workflow_name"] = "Release Flow"
                }
            },
            Skill: new SkillManifest(
                SkillId: "shipping-and-launch",
                Name: "Shipping and Launch",
                Description: "Ship safely.",
                Version: "1.0.0",
                InvocationRules: ["deploy", "verify"],
                RequiredFiles: ["checklists/release.md"],
                OptionalTools: ["rg", "git"],
                Content: "Always verify health checks.",
                Fingerprint: new string('a', 64),
                FilePath: "/skills/shipping-and-launch/SKILL.md")));

        Assert.Equal("Release Flow", result.PromptSnapshot.Variables["workflow_name"]);
        Assert.Contains("Deploy Release Flow to staging on attempt 2.", result.PromptSnapshot.FinalPrompt);
        Assert.Contains(result.PromptSnapshot.Sections, static s => s.Name == "task_prompt");
        Assert.Contains(result.PromptSnapshot.Sections, static s => s.Name == "skill_context");
        Assert.Contains(result.PromptSnapshot.Sections, static s => s.Name == "runtime_context");
        Assert.Contains("Version: 1.0.0", result.PromptSnapshot.FinalPrompt);
        Assert.Contains("OptionalTools: rg, git", result.PromptSnapshot.FinalPrompt);
    }

    [Fact]
    public void Assemble_WithPromptFile_LoadsFileAndTracksSource()
    {
        var assembler = new AgentPromptAssembler();
        var promptFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.prompt.md");

        try
        {
            File.WriteAllText(promptFile, "Review {{node_name}} for {{policy_tag}}.");

            var result = assembler.Assemble(new AgentPromptAssemblyRequest(
                RunId: "run-1",
                StepId: "step-1",
                NodeId: "Review",
                NodeName: "Security Review",
                AgentName: "security-agent",
                AgentDescription: "Performs security review.",
                AgentCategory: "security",
                Action: "security.scan",
                Environment: "production",
                PurposeType: "security_review",
                PolicyTag: "security-scan",
                Attempt: 1,
                RequiresEvidence: [],
                Prompt: new AgentPromptContract
                {
                    File = promptFile
                }));

            Assert.Contains(promptFile, result.PromptSnapshot.SourceFiles);
            Assert.Contains("Review Security Review for security-scan.", result.PromptSnapshot.FinalPrompt);
        }
        finally
        {
            if (File.Exists(promptFile))
            {
                File.Delete(promptFile);
            }
        }
    }

    [Fact]
    public void Assemble_WithMissingVariable_LeavesPlaceholderAndRecordsMissingName()
    {
        var assembler = new AgentPromptAssembler();

        var result = assembler.Assemble(new AgentPromptAssemblyRequest(
            RunId: "run-1",
            StepId: "step-1",
            NodeId: "Node",
            NodeName: "Node",
            AgentName: "test-agent",
            AgentDescription: "Runs tests.",
            AgentCategory: "quality",
            Action: "run-tests",
            Environment: "dev",
            PurposeType: "verification",
            PolicyTag: "test-gate",
            Attempt: 1,
            RequiresEvidence: [],
            Prompt: new AgentPromptContract
            {
                Inline = "Value: {{missing_value}}"
            }));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.MissingVariables);
        Assert.Equal("missing_value", Assert.Single(result.MissingVariables!));
        Assert.Equal("missing_value", Assert.Single(result.PromptSnapshot.MissingVariables));
        Assert.Contains("Value: {{missing_value}}", result.PromptSnapshot.FinalPrompt);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Assemble_WithStrictMissingVariable_ReturnsFailure()
    {
        var assembler = new AgentPromptAssembler();

        var result = assembler.Assemble(new AgentPromptAssemblyRequest(
            RunId: "run-1",
            StepId: "step-1",
            NodeId: "Node",
            NodeName: "Node",
            AgentName: "test-agent",
            AgentDescription: "Runs tests.",
            AgentCategory: "quality",
            Action: "run-tests",
            Environment: "dev",
            PurposeType: "verification",
            PolicyTag: "test-gate",
            Attempt: 1,
            RequiresEvidence: [],
            Prompt: new AgentPromptContract
            {
                Inline = "Value: {{missing_value}}",
                StrictVariables = true
            }));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.MissingVariables);
        Assert.Equal("missing_value", Assert.Single(result.MissingVariables!));
        Assert.Equal("missing_value", Assert.Single(result.PromptSnapshot.MissingVariables));
        Assert.Contains("missing variables", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assemble_WithRunContext_RendersSectionAndExposesVariables()
    {
        var assembler = new AgentPromptAssembler();

        var result = assembler.Assemble(new AgentPromptAssemblyRequest(
            RunId: "run-1",
            StepId: "step-1",
            NodeId: "DesignArchitecture",
            NodeName: "Design Architecture",
            AgentName: "solution-architect",
            AgentDescription: "Produces a technical design.",
            AgentCategory: "architecture",
            Action: "architecture-design",
            Environment: null,
            PurposeType: "architecture-design",
            PolicyTag: "doc-generation",
            Attempt: 1,
            RequiresEvidence: [],
            Prompt: new AgentPromptContract
            {
                Inline = "Design from requirements: {{output.WriteRequirements}}"
            },
            RunContext: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["input.title"] = "Add dark mode",
                ["input.body"] = "Users want a dark theme toggle.",
                ["output.WriteRequirements"] = "REQ-1: provide a theme toggle."
            }));

        Assert.True(result.Succeeded);
        // Context entries are usable as template variables.
        Assert.Contains("Design from requirements: REQ-1: provide a theme toggle.", result.PromptSnapshot.FinalPrompt);
        // And surfaced as a dedicated run_context section.
        Assert.Contains(result.PromptSnapshot.Sections, static s => s.Name == "run_context");
        Assert.Contains("### input.title", result.PromptSnapshot.FinalPrompt);
        Assert.Contains("Users want a dark theme toggle.", result.PromptSnapshot.FinalPrompt);
    }

    [Fact]
    public void Assemble_WithoutRunContext_OmitsSection()
    {
        var assembler = new AgentPromptAssembler();

        var result = assembler.Assemble(new AgentPromptAssemblyRequest(
            RunId: "run-1",
            StepId: "step-1",
            NodeId: "Node",
            NodeName: "Node",
            AgentName: "test-agent",
            AgentDescription: "Runs tests.",
            AgentCategory: "quality",
            Action: "run-tests",
            Environment: "dev",
            PurposeType: "verification",
            PolicyTag: "test-gate",
            Attempt: 1,
            RequiresEvidence: []));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(result.PromptSnapshot.Sections, static s => s.Name == "run_context");
    }

    [Fact]
    public void Assemble_WithSameInput_IsDeterministic()
    {
        var assembler = new AgentPromptAssembler();
        var request = new AgentPromptAssemblyRequest(
            RunId: "run-1",
            StepId: "step-1",
            NodeId: "Node",
            NodeName: "Node Name",
            AgentName: "test-agent",
            AgentDescription: "Runs tests.",
            AgentCategory: "quality",
            Action: "run-tests",
            Environment: "dev",
            PurposeType: "verification",
            PolicyTag: "test-gate",
            Attempt: 1,
            RequiresEvidence: ["coverage_report"],
            Prompt: new AgentPromptContract
            {
                Inline = "Check {{node_name}} with {{requires_evidence_csv}}."
            });

        var first = assembler.Assemble(request);
        var second = assembler.Assemble(request);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(first.PromptSnapshot.FinalPrompt, second.PromptSnapshot.FinalPrompt);
        Assert.Equal(
            first.PromptSnapshot.Sections.Select(static section => section.Content),
            second.PromptSnapshot.Sections.Select(static section => section.Content));
    }
}

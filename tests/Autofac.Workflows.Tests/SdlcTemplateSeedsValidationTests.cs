using System.Text.RegularExpressions;
using Autofac.Application.Workflows;
using Autofac.Workflows.Bpmn;

namespace Autofac.Workflows.Tests;

/// <summary>
/// Every built-in SDLC template must validate cleanly against the real BPMN validator — the
/// template catalog's own tests only check metadata via a stub authoring service, so a typo in
/// hand-authored BPMN XML (e.g. issue #141's 7-stage autonomous-sdlc template) would otherwise
/// only be caught the first time someone tries to clone it from the UI.
/// </summary>
public sealed class SdlcTemplateSeedsValidationTests
{
    public static IEnumerable<object[]> AllTemplateIds() =>
        SdlcTemplateSeeds.All.Select(static t => new object[] { t.Id });

    [Theory]
    [MemberData(nameof(AllTemplateIds))]
    public void Template_ValidatesWithoutErrors(string templateId)
    {
        var template = SdlcTemplateSeeds.All.Single(t => t.Id == templateId);
        var validator = new BpmnWorkflowValidator();

        var result = validator.Validate(template.BpmnXml);

        Assert.True(
            result.IsValid,
            $"Template '{templateId}' failed validation: {string.Join("; ", result.Errors.Select(static e => e.Message))}");
        Assert.NotNull(result.Definition);
    }

    [Fact]
    public void AutonomousSdlcTemplate_HasAllSevenStagesInOrder()
    {
        var template = SdlcTemplateSeeds.All.Single(static t => t.Id == "autonomous-sdlc");
        var result = new BpmnWorkflowValidator().Validate(template.BpmnXml);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(static e => e.Message)));
        var definition = result.Definition!;

        var nodeIds = definition.Nodes.Select(static n => n.Id).ToArray();
        Assert.Equal(
            new[]
            {
                "Start", "RequirementDesign", "RequirementApproval", "ArchitectureDesign", "ArchitectureApproval",
                "TechnicalAnalysis", "Implementation", "SeniorReview", "WaitForMerge", "TriggerDeploy",
                "WaitForCiGreen", "Test", "End"
            },
            nodeIds);

        var requirementApproval = definition.Nodes.Single(static n => n.Id == "RequirementApproval");
        Assert.Equal("userTask", requirementApproval.ElementName);
        Assert.NotNull(requirementApproval.ApprovalMetadata);

        var architectureApproval = definition.Nodes.Single(static n => n.Id == "ArchitectureApproval");
        Assert.NotNull(architectureApproval.ApprovalMetadata);

        var implementation = definition.Nodes.Single(static n => n.Id == "Implementation");
        Assert.Equal("agent_sandboxed", implementation.Metadata!.ExecutionMode);
        Assert.Equal("repo-write", implementation.Metadata.SandboxProfile);

        var seniorReview = definition.Nodes.Single(static n => n.Id == "SeniorReview");
        Assert.Equal("agent_sandboxed", seniorReview.Metadata!.ExecutionMode);
        Assert.Equal("repo-read", seniorReview.Metadata.SandboxProfile);

        var test = definition.Nodes.Single(static n => n.Id == "Test");
        Assert.Equal("agent_sandboxed", test.Metadata!.ExecutionMode);

        var waitForMerge = definition.Nodes.Single(static n => n.Id == "WaitForMerge");
        Assert.Equal("intermediateCatchEvent", waitForMerge.ElementName);
        Assert.Equal("github.pull_request.merged", waitForMerge.ExternalEventMetadata!.MessageName);
        Assert.Equal("{{input.branch_name}}", waitForMerge.ExternalEventMetadata.CorrelationKeyTemplate);

        var triggerDeploy = definition.Nodes.Single(static n => n.Id == "TriggerDeploy");
        Assert.Equal("cicd.trigger_deploy", triggerDeploy.Metadata!.Action);

        var waitForCiGreen = definition.Nodes.Single(static n => n.Id == "WaitForCiGreen");
        Assert.Equal("github.workflow_run.completed", waitForCiGreen.ExternalEventMetadata!.MessageName);
    }

    [Fact]
    public void FirstRunDockerSeedWorkflow_ValidatesWithoutErrors()
    {
        var seedSql = File.ReadAllText(FindRepositoryFile("docker", "seed-first-run.sql"));
        var match = Regex.Match(seedSql, @"\$bpmn\$(?<xml>.*?)\$bpmn\$", RegexOptions.Singleline);
        Assert.True(match.Success, "docker/seed-first-run.sql must dollar-quote the seeded BPMN XML.");

        var result = new BpmnWorkflowValidator().Validate(match.Groups["xml"].Value);

        Assert.True(
            result.IsValid,
            $"First-run seed workflow failed validation: {string.Join("; ", result.Errors.Select(static e => e.Message))}");
        Assert.NotNull(result.Definition);

        var agentTask = result.Definition.Nodes.Single(static n => n.Id == "DraftImplementationNote");
        Assert.Equal("first-run-engineer", agentTask.Metadata!.Agent);
        Assert.Equal("first-run.implement", agentTask.Metadata.Action);
        Assert.Equal("local", agentTask.Metadata.ExecutionMode);

        var approvalTask = result.Definition.Nodes.Single(static n => n.Id == "ReviewSampleOutput");
        Assert.NotNull(approvalTask.ApprovalMetadata);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Autofac.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (Autofac.sln) from the test output directory.");
        }

        return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
    }
}

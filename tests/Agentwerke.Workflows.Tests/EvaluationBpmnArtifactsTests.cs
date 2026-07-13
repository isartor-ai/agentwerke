using System.Xml.Linq;
using Agentwerke.Workflows.Bpmn;

namespace Agentwerke.Workflows.Tests;

public sealed class EvaluationBpmnArtifactsTests
{
    [Fact]
    public void VModelProcessEvaluationArtifact_ValidatesAndCapturesRequiredAgentwerkeMetadata()
    {
        var bpmn = File.ReadAllText(FindRepositoryFile("examples", "v-model-process.bpmn"));

        var result = new BpmnWorkflowValidator().Validate(bpmn);

        Assert.True(
            result.IsValid,
            $"V-model evaluation BPMN failed validation: {string.Join("; ", result.Errors.Select(static e => e.Message))}");
        Assert.NotNull(result.Definition);

        var definition = result.Definition!;
        Assert.Equal("VModelProcessEvaluation", definition.ProcessId);
        Assert.Equal(
            new[]
            {
                "Start",
                "DraftRequirements",
                "ApproveRequirementsBaseline",
                "DraftSystemArchitecture",
                "DraftComponentDesign",
                "ImplementComponents",
                "GenerateUnitTestPlan",
                "WaitUnitTestResults",
                "ValidateComponentTraceability",
                "GenerateIntegrationTestPlan",
                "WaitIntegrationTestResults",
                "ValidateArchitectureTraceability",
                "AnalyzeSystemTestResults",
                "WaitSystemTestResults",
                "SummarizeVerificationFailures",
                "PrepareAcceptanceTest",
                "ApproveAcceptanceSignoff",
                "PrepareTraceabilityReport",
                "End"
            },
            definition.Nodes.Select(static node => node.Id).ToArray());

        var requirements = definition.Nodes.Single(static node => node.Id == "DraftRequirements");
        Assert.Equal("analyst", requirements.Metadata!.Agent);
        Assert.Equal("vmodel.requirements.draft", requirements.Metadata.Action);
        Assert.Equal("requirements", requirements.Metadata.RuntimeContract!.Metadata["phase"]);

        var implementation = definition.Nodes.Single(static node => node.Id == "ImplementComponents");
        Assert.Equal("agent_sandboxed", implementation.Metadata!.ExecutionMode);
        Assert.Equal("repo-write", implementation.Metadata.SandboxProfile);

        var approvalNodes = definition.Nodes.Where(static node => node.ApprovalMetadata is not null).ToArray();
        Assert.Equal(["ApproveRequirementsBaseline", "ApproveAcceptanceSignoff"], approvalNodes.Select(static node => node.Id));

        AssertExternalWait(definition, "WaitUnitTestResults", "test.unit.completed", "{{input.build_id}}:unit");
        AssertExternalWait(definition, "WaitIntegrationTestResults", "test.integration.completed", "{{input.build_id}}:integration");
        AssertExternalWait(definition, "WaitSystemTestResults", "test.system.completed", "{{input.build_id}}:system");

        var traceability = definition.Nodes.Single(static node => node.Id == "PrepareTraceabilityReport");
        Assert.Equal("vmodel.traceability.report", traceability.Metadata!.Action);
        Assert.Contains("requirements_baseline", traceability.Metadata.RequiresEvidence);
        Assert.Contains("acceptance_signoff", traceability.Metadata.RequiresEvidence);
    }

    [Fact]
    public void VModelProcessEvaluationArtifact_WhenAgentMetadataIsRemoved_ReturnsActionableValidationError()
    {
        var bpmn = File.ReadAllText(FindRepositoryFile("examples", "v-model-process.bpmn"));
        var invalidBpmn = RemoveExtensionElements(bpmn, "GenerateUnitTestPlan");

        var result = new BpmnWorkflowValidator().Validate(invalidBpmn);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("GenerateUnitTestPlan", error.ElementId);
        Assert.Contains("requires agentwerke:agentTask metadata", error.Message, StringComparison.Ordinal);
    }

    private static void AssertExternalWait(
        BpmnWorkflowDefinition definition,
        string nodeId,
        string messageName,
        string correlationKeyTemplate)
    {
        var node = definition.Nodes.Single(node => node.Id == nodeId);

        Assert.Equal("intermediateCatchEvent", node.ElementName);
        Assert.Equal(messageName, node.ExternalEventMetadata!.MessageName);
        Assert.Equal(correlationKeyTemplate, node.ExternalEventMetadata.CorrelationKeyTemplate);
    }

    private static string RemoveExtensionElements(string bpmn, string nodeId)
    {
        var document = XDocument.Parse(bpmn);
        var node = document
            .Descendants()
            .Single(element => string.Equals((string?)element.Attribute("id"), nodeId, StringComparison.Ordinal));
        node.Elements().Single(static element => element.Name.LocalName == "extensionElements").Remove();

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Agentwerke.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (Agentwerke.sln) from the test output directory.");
        }

        return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
    }
}

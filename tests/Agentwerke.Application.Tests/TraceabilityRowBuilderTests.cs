using System.Text.Json;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;

namespace Agentwerke.Application.Tests;

public sealed class TraceabilityRowBuilderTests
{
    [Fact]
    public void Build_LinksTheRequirementToEachTestCaseTheCiRunReported()
    {
        var run = CreateRun(
            requirementAction: ExternalAction("github", "read_issue", "42", "https://github.com/octo/app/issues/42"),
            ingestOutput: IngestOutput(
                ciRunUrl: "https://github.com/octo/app/actions/runs/17482",
                cases: [Case("Tests.OrderTests.CreatesOrder", "CreatesOrder", "passed")]));

        var row = Assert.Single(TraceabilityRowBuilder.Build(run));

        // Every external field resolves to a real record, which is the property #210 asks for.
        Assert.Equal("github", row.RequirementProvider);
        Assert.Equal("42", row.RequirementId);
        Assert.Equal("https://github.com/octo/app/issues/42", row.RequirementUrl);
        Assert.Equal("Tests.OrderTests.CreatesOrder", row.TestId);
        Assert.Equal("CreatesOrder", row.TestName);
        Assert.Equal("17482", row.CiRunId);
        Assert.Equal("https://github.com/octo/app/actions/runs/17482", row.CiRunUrl);
        Assert.Equal("passed", row.Status);
        Assert.Equal("verification-step-1-junit.xml", row.EvidenceArtifact);
    }

    [Fact]
    public void Build_ProducesARowPerTestCase()
    {
        var run = CreateRun(
            requirementAction: ExternalAction("github", "read_issue", "42", "https://github.com/octo/app/issues/42"),
            ingestOutput: IngestOutput(cases:
            [
                Case("Tests.OrderTests.CreatesOrder", "CreatesOrder", "passed"),
                Case("Tests.OrderTests.RejectsEmptyCart", "RejectsEmptyCart", "failed", "expected 400, got 200"),
                Case("Tests.OrderTests.AppliesDiscount", "AppliesDiscount", "skipped")
            ]));

        var rows = TraceabilityRowBuilder.Build(run);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal("42", row.RequirementId));
        Assert.Equal(["passed", "failed", "skipped"], rows.Select(static r => r.Status));
        Assert.Equal("expected 400, got 200", rows[1].FailureMessage);
    }

    /// <summary>
    /// A run that has not reached verification has no rows — which is a different answer from the run
    /// being untraceable, and must not be dressed up as one.
    /// </summary>
    [Fact]
    public void Build_BeforeAnyIngest_ReturnsNoRows()
    {
        var run = CreateRun(
            requirementAction: ExternalAction("github", "read_issue", "42", "https://github.com/octo/app/issues/42"),
            ingestOutput: null);

        Assert.Empty(TraceabilityRowBuilder.Build(run));
    }

    /// <summary>
    /// Results without a requirement are still rows: the tests demonstrably ran, and hiding them
    /// because the requirement link is missing would lose real evidence. The gap shows as nulls.
    /// </summary>
    [Fact]
    public void Build_WithoutARequirementRead_StillReportsTheTestsWithAnEmptyRequirementLink()
    {
        var run = CreateRun(
            requirementAction: null,
            ingestOutput: IngestOutput(cases: [Case("Tests.T", "T", "passed")]));

        var row = Assert.Single(TraceabilityRowBuilder.Build(run));
        Assert.Null(row.RequirementId);
        Assert.Null(row.RequirementProvider);
        Assert.Equal("Tests.T", row.TestId);
    }

    /// <summary>Most steps are not ingests; their output must not be mistaken for test results.</summary>
    [Fact]
    public void Build_IgnoresStepOutputsThatAreNotIngestResults()
    {
        var run = CreateRun(
            requirementAction: ExternalAction("github", "read_issue", "42", "https://github.com/octo/app/issues/42"),
            ingestOutput: IngestOutput(cases: [Case("Tests.T", "T", "passed")]));

        run.Steps.Add(new WorkflowRunStep { Id = "s-agent", Name = "ImplementChange", Output = "I changed some files." });
        run.Steps.Add(new WorkflowRunStep { Id = "s-json", Name = "ReadRequirement", Output = """{"provider":"github","action":"read_issue","issue_number":42}""" });
        run.Steps.Add(new WorkflowRunStep { Id = "s-null", Name = "Noop", Output = null });

        Assert.Single(TraceabilityRowBuilder.Build(run));
    }

    [Theory]
    [InlineData("https://github.com/octo/app/actions/runs/17482", "17482")]
    [InlineData("https://github.com/octo/app/actions/runs/17482/job/55", "17482")]
    // An unfamiliar CI system: the URL still links, but a guessed id would be worse than none.
    [InlineData("https://ci.example/build/17482", null)]
    [InlineData("not-a-url", null)]
    public void Build_DerivesTheCiRunIdOnlyWhenTheUrlActuallyCarriesOne(string ciRunUrl, string? expected)
    {
        var run = CreateRun(
            requirementAction: null,
            ingestOutput: IngestOutput(ciRunUrl: ciRunUrl, cases: [Case("Tests.T", "T", "passed")]));

        Assert.Equal(expected, Assert.Single(TraceabilityRowBuilder.Build(run)).CiRunId);
    }

    [Fact]
    public void Build_WhenAnEventIsNotReadable_SkipsItRatherThanFailingTheWholeMatrix()
    {
        var run = CreateRun(
            requirementAction: ExternalAction("github", "read_issue", "42", "https://github.com/octo/app/issues/42"),
            ingestOutput: IngestOutput(cases: [Case("Tests.T", "T", "passed")]));

        run.Events.Add(new WorkflowEvent { Id = "e-bad", Type = "external_action_recorded", Message = "not json" });

        Assert.Equal("42", Assert.Single(TraceabilityRowBuilder.Build(run)).RequirementId);
    }

    private static WorkflowRun CreateRun(WorkflowEvent? requirementAction, string? ingestOutput)
    {
        var run = new WorkflowRun { Id = "run_abc123", WorkflowId = "VModelPilotThread" };

        if (requirementAction is not null)
        {
            run.Events.Add(requirementAction);
        }

        if (ingestOutput is not null)
        {
            run.Steps.Add(new WorkflowRunStep
            {
                Id = "step-1",
                Name = "IngestTestResults",
                Status = "completed",
                Output = ingestOutput
            });
        }

        return run;
    }

    /// <summary>
    /// Shaped exactly as WorkflowInstanceEngine writes an "external_action_recorded" event — see the
    /// Serialize(new { ... }) call there. If that payload changes, this fixture is the thing that
    /// should be updated with it.
    /// </summary>
    private static WorkflowEvent ExternalAction(string provider, string action, string resourceId, string resourceUrl) =>
        new()
        {
            Id = $"e-{action}",
            Type = "external_action_recorded",
            Message = JsonSerializer.Serialize(new
            {
                runId = "run_abc123",
                nodeId = "ReadRequirement",
                stepId = "step-0",
                provider,
                action,
                status = "completed",
                resourceId,
                resourceUrl,
                summary = $"GitHub issue #{resourceId}",
                attempt = 1
            })
        };

    private static string IngestOutput(
        IReadOnlyList<string> cases,
        string? ciRunUrl = "https://github.com/octo/app/actions/runs/17482") =>
        $$"""
        {
          "provider": "ci",
          "action": "ingest_test_results",
          "format": "junit",
          "status": "passed",
          "total": {{cases.Count}},
          "passed": {{cases.Count}},
          "failed": 0,
          "errors": 0,
          "skipped": 0,
          "duration_ms": 42.0,
          "incomplete": false,
          "declared_total": {{cases.Count}},
          "ci_run_url": {{(ciRunUrl is null ? "null" : $"\"{ciRunUrl}\"")}},
          "report_url": null,
          "report_artifact": "verification-step-1-junit.xml",
          "cases": [{{string.Join(",", cases)}}]
        }
        """;

    private static string Case(string id, string name, string status, string? failureMessage = null) =>
        $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "class_name": "Tests.OrderTests",
          "suite": "Example.Tests.dll",
          "status": "{{status}}",
          "duration_ms": 1.0,
          "failure_type": null,
          "failure_message": {{(failureMessage is null ? "null" : $"\"{failureMessage}\"")}},
          "failure_detail": null
        }
        """;
}

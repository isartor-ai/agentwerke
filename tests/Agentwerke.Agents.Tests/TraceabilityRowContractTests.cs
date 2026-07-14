using System.Text;
using Agentwerke.Agents.Tools;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Agentwerke.Storage.Artifacts;

namespace Agentwerke.Agents.Tests;

/// <summary>
/// Pins the contract between the ingest tool and the traceability row (#210). The tool writes its
/// results into a step's output; the row builder reads them back out in another module, and neither
/// calls the other — the step output is the only thing between them.
///
/// Both sides' own tests use fixtures, so both could agree with their fixture and disagree with each
/// other. That failure is silent: the row builder would find no ingest output, and the matrix would
/// simply render empty — no exception, no failing test, just a run that looks unverified. So this
/// runs the real tool and hands its real output to the real builder.
/// </summary>
public sealed class TraceabilityRowContractTests
{
    [Fact]
    public async Task TheIngestToolsOutput_IsReadableByTheTraceabilityRowBuilder()
    {
        var toolOutput = await IngestAsync("""
            <testsuites>
              <testsuite name="Example.Tests.dll" tests="2" failures="1" time="0.42">
                <testcase classname="Example.Tests.OrderTests" name="CreatesOrder" time="0.011" />
                <testcase classname="Example.Tests.OrderTests" name="RejectsEmptyCart" time="0.009">
                  <failure message="expected 400, got 200" type="Xunit.EqualException">at OrderTests.cs:31</failure>
                </testcase>
              </testsuite>
            </testsuites>
            """);

        var run = new WorkflowRun { Id = "run_abc123", WorkflowId = "VModelPilotThread" };
        run.Events.Add(new WorkflowEvent
        {
            Id = "e-read",
            Type = "external_action_recorded",
            Message = """
                {"runId":"run_abc123","nodeId":"ReadRequirement","stepId":"step-0","provider":"github",
                 "action":"read_issue","status":"completed","resourceId":"42",
                 "resourceUrl":"https://github.com/octo/app/issues/42","summary":"GitHub issue #42","attempt":1}
                """
        });
        run.Steps.Add(new WorkflowRunStep
        {
            Id = "step-ingest",
            Name = "IngestTestResults",
            Status = "completed",
            Output = toolOutput
        });

        var rows = TraceabilityRowBuilder.Build(run);

        Assert.Equal(2, rows.Count);

        var passed = rows.Single(r => r.TestId == "Example.Tests.OrderTests.CreatesOrder");
        Assert.Equal("github", passed.RequirementProvider);
        Assert.Equal("42", passed.RequirementId);
        Assert.Equal("https://github.com/octo/app/issues/42", passed.RequirementUrl);
        Assert.Equal("passed", passed.Status);
        Assert.Equal("17482", passed.CiRunId);
        Assert.Equal("https://github.com/octo/app/actions/runs/17482", passed.CiRunUrl);
        // The artifact the tool actually stored, so the row points at retrievable evidence.
        Assert.Equal("verification-step-ingest-junit.xml", passed.EvidenceArtifact);

        var failed = rows.Single(r => r.TestId == "Example.Tests.OrderTests.RejectsEmptyCart");
        Assert.Equal("failed", failed.Status);
        Assert.Equal("expected 400, got 200", failed.FailureMessage);
    }

    /// <summary>
    /// A red build still produces rows: the thread's job is to record what verification found, not to
    /// only record good news.
    /// </summary>
    [Fact]
    public async Task AFailingBuild_StillProducesTraceabilityRows()
    {
        var toolOutput = await IngestAsync("""
            <testsuite name="s" tests="1" failures="1">
              <testcase classname="c" name="t"><failure message="boom" /></testcase>
            </testsuite>
            """);

        var run = new WorkflowRun { Id = "run_abc123" };
        run.Steps.Add(new WorkflowRunStep { Id = "step-ingest", Name = "IngestTestResults", Output = toolOutput });

        var row = Assert.Single(TraceabilityRowBuilder.Build(run));
        Assert.Equal("failed", row.Status);
        Assert.Equal("boom", row.FailureMessage);
    }

    private static async Task<string> IngestAsync(string reportXml)
    {
        var result = await new VerificationIngestTestResultsTool(new NoOpArtifactStorage()).ExecuteAsync(
            new AgentToolExecutionContext(
                RunId: "run_abc123",
                StepId: "step-ingest",
                AgentName: "ci",
                Action: "verification.ingest_test_results",
                Environment: "github",
                PurposeType: "verification_ingest",
                PolicyTag: "vmodel-pilot-verification",
                Attempt: 1),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["report_xml"] = reportXml,
                ["ci_run_url"] = "https://github.com/octo/app/actions/runs/17482",
            },
            CancellationToken.None);

        return result.Output!;
    }

    private sealed class NoOpArtifactStorage : IArtifactStorage
    {
        public Task SaveAsync(string runId, string artifactName, Stream content, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ArtifactDescriptor>> ListAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string runId, string artifactName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(string runId, string artifactName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

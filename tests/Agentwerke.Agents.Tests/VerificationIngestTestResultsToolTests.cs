using System.Text;
using System.Text.Json;
using Agentwerke.Agents.Tools;
using Agentwerke.Storage.Artifacts;

namespace Agentwerke.Agents.Tests;

public sealed class VerificationIngestTestResultsToolTests
{
    private const string PassingReport = """
        <testsuites>
          <testsuite name="Example.Tests.dll" tests="2" failures="0" time="0.42">
            <testcase classname="Example.Tests.OrderTests" name="CreatesOrder" time="0.011" />
            <testcase classname="Example.Tests.OrderTests" name="RejectsEmptyCart" time="0.009" />
          </testsuite>
        </testsuites>
        """;

    private const string FailingReport = """
        <testsuites>
          <testsuite name="Example.Tests.dll" tests="2" failures="1" time="0.42">
            <testcase classname="Example.Tests.OrderTests" name="CreatesOrder" time="0.011" />
            <testcase classname="Example.Tests.OrderTests" name="RejectsEmptyCart" time="0.009">
              <failure message="expected 400, got 200" type="Xunit.EqualException">at OrderTests.cs:31</failure>
            </testcase>
          </testsuite>
        </testsuites>
        """;

    [Fact]
    public async Task ExecuteAsync_ParsesTheReportIntoStructuredOutput()
    {
        var storage = new RecordingArtifactStorage();
        var result = await Execute(storage, PassingReport, ciRunUrl: "https://ci.example/runs/9");

        Assert.True(result.Succeeded);

        var output = JsonDocument.Parse(result.Output!).RootElement;
        Assert.Equal("passed", output.GetProperty("status").GetString());
        Assert.Equal(2, output.GetProperty("total").GetInt32());
        Assert.Equal(2, output.GetProperty("passed").GetInt32());
        Assert.Equal(0, output.GetProperty("failed").GetInt32());
        Assert.Equal("https://ci.example/runs/9", output.GetProperty("ci_run_url").GetString());

        // The individual cases, not just the totals — a traceability row links to one test id.
        var cases = output.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(2, cases.Length);
        Assert.Equal("Example.Tests.OrderTests.CreatesOrder", cases[0].GetProperty("id").GetString());
        Assert.Equal("passed", cases[0].GetProperty("status").GetString());
    }

    /// <summary>
    /// The parse is Agentwerke's reading of the evidence; the artifact is the evidence. A reviewer
    /// disputing a row needs the document the runner emitted, byte for byte.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_StoresTheRawReportVerbatimAsAnArtifact()
    {
        var storage = new RecordingArtifactStorage();
        var result = await Execute(storage, PassingReport);

        var (runId, name, content) = Assert.Single(storage.Saved);
        Assert.Equal("run_abc123", runId);
        Assert.Equal("verification-step-ingest-junit.xml", name);
        Assert.Equal(PassingReport, content);

        // And it is referenced from the result, so it reaches the evidence pack.
        Assert.Contains("verification-step-ingest-junit.xml", result.Artifacts!.Keys);
    }

    /// <summary>
    /// A red build is the result the thread exists to record. Failing the step on it would lose that
    /// result and look like an ingestion fault instead.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTestsFailed_StillSucceedsButReportsTheFailure()
    {
        var storage = new RecordingArtifactStorage();
        var result = await Execute(storage, FailingReport);

        Assert.True(result.Succeeded);

        var output = JsonDocument.Parse(result.Output!).RootElement;
        Assert.Equal("failed", output.GetProperty("status").GetString());
        Assert.Equal(1, output.GetProperty("failed").GetInt32());

        var failedCase = output.GetProperty("cases").EnumerateArray()
            .Single(c => c.GetProperty("status").GetString() == "failed");
        Assert.Equal("expected 400, got 200", failedCase.GetProperty("failure_message").GetString());
        Assert.Equal("Xunit.EqualException", failedCase.GetProperty("failure_type").GetString());

        // The external action carries the CI outcome, not the ingest's — a row reads this.
        var action = Assert.Single(result.ExternalActions!);
        Assert.Equal("failed", action.Status);
        Assert.Contains("1 failed", action.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsTheCiRunAndReportUrlsOnTheExternalAction()
    {
        var result = await Execute(
            new RecordingArtifactStorage(),
            PassingReport,
            ciRunUrl: "https://ci.example/runs/9",
            reportUrl: "https://ci.example/runs/9/junit.xml");

        var action = Assert.Single(result.ExternalActions!);
        Assert.Equal("ci", action.Provider);
        Assert.Equal("https://ci.example/runs/9", action.ResourceId);
        Assert.Equal("https://ci.example/runs/9/junit.xml", action.ResourceUrl);
    }

    /// <summary>
    /// Carrying on with no results would leave the run looking verified while holding no verification.
    /// The realistic case is an artifact URL that returned an HTML error page.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTheReportIsNotATestReport_FailsRatherThanIngestingNothing()
    {
        var storage = new RecordingArtifactStorage();

        await Assert.ThrowsAsync<Agentwerke.Domain.Verification.TestResultParseException>(() =>
            Execute(storage, "<html><body>404 Not Found</body></html>"));

        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task ExecuteAsync_AnUnsupportedFormat_IsRejectedRatherThanParsedAsJUnitAnyway()
    {
        var input = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["report_xml"] = PassingReport,
            ["format"] = "trx",
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new VerificationIngestTestResultsTool(new RecordingArtifactStorage())
                .ExecuteAsync(Context(), input, CancellationToken.None));

        Assert.Contains("only 'junit' is supported", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_AReportOverTheIngestLimit_IsRejected()
    {
        var oversized = "<testsuites>" + new string('x', 9 * 1024 * 1024) + "</testsuites>";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Execute(new RecordingArtifactStorage(), oversized));

        Assert.Contains("ingest limit", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A truncated report must not read as a complete one — it is the difference between "all tests
    /// passed" and "every test we happened to receive passed".
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ATruncatedReport_IsFlaggedInTheEvidence()
    {
        var result = await Execute(new RecordingArtifactStorage(), """
            <testsuite name="s" tests="10">
              <testcase classname="c" name="t" />
            </testsuite>
            """);

        var output = JsonDocument.Parse(result.Output!).RootElement;
        Assert.True(output.GetProperty("incomplete").GetBoolean());
        Assert.Equal(10, output.GetProperty("declared_total").GetInt32());

        Assert.Contains("may be truncated", Assert.Single(result.ExternalActions!).Summary, StringComparison.Ordinal);
    }

    private static Task<AgentToolExecutionResult> Execute(
        IArtifactStorage storage,
        string reportXml,
        string? ciRunUrl = null,
        string? reportUrl = null)
    {
        var input = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["report_xml"] = reportXml,
        };

        if (ciRunUrl is not null)
        {
            input["ci_run_url"] = ciRunUrl;
        }

        if (reportUrl is not null)
        {
            input["report_url"] = reportUrl;
        }

        return new VerificationIngestTestResultsTool(storage).ExecuteAsync(Context(), input, CancellationToken.None);
    }

    private static AgentToolExecutionContext Context() =>
        new(
            RunId: "run_abc123",
            StepId: "step-ingest",
            AgentName: "ci",
            Action: "verification.ingest_test_results",
            Environment: "github",
            PurposeType: "verification_ingest",
            PolicyTag: "vmodel-pilot-verification",
            Attempt: 1);

    private sealed class RecordingArtifactStorage : IArtifactStorage
    {
        public List<(string RunId, string Name, string Content)> Saved { get; } = [];

        public async Task SaveAsync(string runId, string artifactName, Stream content, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(content, Encoding.UTF8);
            Saved.Add((runId, artifactName, await reader.ReadToEndAsync(cancellationToken)));
        }

        public Task<IReadOnlyList<ArtifactDescriptor>> ListAsync(string runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string runId, string artifactName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(string runId, string artifactName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

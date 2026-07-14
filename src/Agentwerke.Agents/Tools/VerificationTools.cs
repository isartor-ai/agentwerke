using System.Text;
using System.Text.Json;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Verification;
using Agentwerke.Storage.Artifacts;
using Agentwerke.Workflows.Runtime;

namespace Agentwerke.Agents.Tools;

/// <summary>
/// Ingests a CI job's test report into the run as structured evidence (#210), so what a run proves
/// about verification is the cases the runner reported rather than an agent's summary of them.
/// </summary>
/// <remarks>
/// Deterministic, no model call: a BPMN node configured with this action dispatches straight here.
/// The report arrives inline, normally from a signed external event — the CI job posts it as the
/// event payload, the resume path lands it in run context, and the node reads
/// <c>{{event.report_xml}}</c>. Inline rather than fetched from an artifact URL on purpose: a URL
/// taken from a webhook payload is caller-controlled, and fetching it would let an event steer the
/// server at arbitrary hosts. A URL is still recorded, as a pointer for a human, never dereferenced.
///
/// The raw document is stored verbatim alongside the parse. The parse is Agentwerke's reading of the
/// evidence; the artifact is the evidence, and a reviewer disputing a row needs the thing the runner
/// actually emitted.
/// </remarks>
public sealed class VerificationIngestTestResultsTool : IAgentTool, IToolSchemaProvider
{
    /// <summary>
    /// Bounds what a single signed event can push into artifact storage. JUnit reports for a suite of
    /// a few thousand tests sit well under this; a report that exceeds it is likelier a mistake, and
    /// silently truncating one would produce evidence that omits cases without saying so.
    /// </summary>
    private const int MaxReportBytes = 8 * 1024 * 1024;

    private readonly IArtifactStorage _artifactStorage;

    public VerificationIngestTestResultsTool(IArtifactStorage artifactStorage)
    {
        _artifactStorage = artifactStorage;
    }

    public string Name => "verification.ingest_test_results";

    public string Category => AgentToolCategories.Integration;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("report_xml", "string", "The raw JUnit XML report, inline. Normally {{event.report_xml}}.", Required: true),
        new("format", "string", "Report format. Only \"junit\" is supported today; defaults to it.", Required: false),
        new("ci_run_url", "string", "URL of the CI run that produced the report. Recorded as evidence.", Required: false),
        new("report_url", "string", "URL of the report artifact in CI. Recorded as a pointer; never fetched.", Required: false)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        GitHubToolInput.Require(input, "report_xml");

        var format = GitHubToolInput.ReadOptional(input, "format");
        if (format is not null && !string.Equals(format, "junit", StringComparison.OrdinalIgnoreCase))
        {
            // Named explicitly and unsupported: fail rather than parse it as JUnit anyway and report
            // whatever that happens to yield.
            throw new InvalidOperationException(
                $"Tool input 'format' is '{format}'; only 'junit' is supported.");
        }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        Validate(input);

        var xml = input["report_xml"];
        if (Encoding.UTF8.GetByteCount(xml) > MaxReportBytes)
        {
            throw new InvalidOperationException(
                $"Test report is larger than the {MaxReportBytes / (1024 * 1024)} MiB ingest limit.");
        }

        // A parse failure fails the step. The alternative — carrying on with no results — would leave
        // the run looking verified while holding no verification at all.
        var result = JUnitTestResultParser.Parse(xml);

        // Flat name, no '/': LocalFileArtifactStorage sanitizes separators to '_' while S3 would treat
        // one as a real prefix, so a path here means two different layouts per backend for no gain.
        var artifactName = $"verification-{context.StepId}-junit.xml";
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
        {
            await _artifactStorage.SaveAsync(context.RunId, artifactName, stream, cancellationToken);
        }

        var ciRunUrl = GitHubToolInput.ReadOptional(input, "ci_run_url");
        var reportUrl = GitHubToolInput.ReadOptional(input, "report_url");

        return new AgentToolExecutionResult(
            // True even when tests failed: ingesting a red report is a successful ingestion, and the
            // outcome belongs in the evidence rather than in whether this step ran. Failing the step
            // on a red build would lose the very result the thread exists to record.
            Succeeded: true,
            Output: JsonSerializer.Serialize(new
            {
                provider = "ci",
                action = "ingest_test_results",
                format = "junit",
                status = result.Succeeded ? "passed" : "failed",
                total = result.Total,
                passed = result.Passed,
                failed = result.Failed,
                errors = result.Errors,
                skipped = result.Skipped,
                duration_ms = result.DurationMs,
                incomplete = result.IsIncomplete,
                declared_total = result.DeclaredTotal,
                ci_run_url = ciRunUrl,
                report_url = reportUrl,
                report_artifact = artifactName,
                cases = result.Cases.Select(static c => new
                {
                    id = c.Id,
                    name = c.Name,
                    class_name = c.ClassName,
                    suite = c.SuiteName,
                    status = c.Status.ToString().ToLowerInvariant(),
                    duration_ms = c.DurationMs,
                    failure_type = c.Failure?.Type,
                    failure_message = c.Failure?.Message,
                    failure_detail = c.Failure?.Detail
                })
            }),
            FailureReason: null,
            Artifacts: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [artifactName] = artifactName
            },
            ExternalActions:
            [
                new ExternalActionRecord(
                    Provider: "ci",
                    Action: "ingest_test_results",
                    // The CI outcome, not the ingest's — this is the record a traceability row reads.
                    Status: result.Succeeded ? "passed" : "failed",
                    ResourceId: ciRunUrl ?? artifactName,
                    ResourceUrl: reportUrl ?? ciRunUrl,
                    Summary: BuildSummary(result))
            ]);
    }

    /// <summary>
    /// Spells out skips rather than folding them into a pass. A suite that skipped the requirement's
    /// test is green and has verified nothing — the distinction has to survive into the evidence.
    /// </summary>
    private static string BuildSummary(TestRunResult result)
    {
        var summary = new StringBuilder()
            .Append(result.Succeeded ? "Tests passed" : "Tests failed")
            .Append(": ")
            .Append(result.Passed).Append(" passed, ")
            .Append(result.Failed).Append(" failed, ")
            .Append(result.Errors).Append(" errored, ")
            .Append(result.Skipped).Append(" skipped, of ")
            .Append(result.Total).Append('.');

        if (result.IsIncomplete)
        {
            summary
                .Append(" Report declared ")
                .Append(result.DeclaredTotal)
                .Append(" cases but contained ")
                .Append(result.Total)
                .Append(" — it may be truncated.");
        }

        return summary.ToString();
    }
}

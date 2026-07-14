using System.Text.Json;
using System.Text.Json.Serialization;
using Agentwerke.Domain.Persistence;
using Agentwerke.Domain.Verification;

namespace Agentwerke.Application.Workflows;

/// <summary>
/// One row of the traceability matrix (#210): a requirement in an external system of record, a test
/// that ran against it, the CI run that produced that test's result, and the stored artifact backing
/// the claim. Every field that identifies something outside Agentwerke is a real external id or URL,
/// which is the whole point — the existing BPMN-derived matrix shows what a workflow *declares*,
/// this shows what a run actually did.
/// </summary>
public sealed record TraceabilityRow(
    string? RequirementProvider,
    string? RequirementId,
    string? RequirementUrl,
    string TestId,
    string TestName,
    string? CiRunId,
    string? CiRunUrl,
    string Status,
    string? EvidenceArtifact,
    string? FailureMessage);

/// <summary>
/// Builds traceability rows from a run's own evidence.
/// </summary>
/// <remarks>
/// Derived rather than stored. A traceability table written alongside the evidence would be a second
/// source of truth that can disagree with it — and a row that disagrees with the evidence pack is
/// worse than no row, because it is the artifact an auditor trusts. Deriving from the recorded
/// external actions and step output means the matrix cannot drift from what the run actually
/// recorded: if the evidence is wrong, the row is visibly wrong too.
///
/// The link between a requirement and a test here is "the verification build dispatched for this
/// requirement produced this case". It is not a semantic claim that this test *verifies* that
/// requirement — nothing in the thread carries a per-test requirement mapping, and inventing one
/// would be exactly the agent-authored traceability #210 rejects. Real per-test mapping needs test
/// annotations and is a separate problem.
/// </remarks>
public static class TraceabilityRowBuilder
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<TraceabilityRow> Build(WorkflowRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var actions = (run.Events ?? [])
            .Where(static e => string.Equals(e.Type, "external_action_recorded", StringComparison.Ordinal))
            .Select(TryReadAction)
            .OfType<ExternalActionEvent>()
            .ToArray();

        var requirement = actions.FirstOrDefault(static a =>
            string.Equals(a.Provider, "github", StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Action, "read_issue", StringComparison.OrdinalIgnoreCase));

        // Every ingested report in the run, not just the first: a re-dispatched build ingests again,
        // and the newest result is the one that describes the run's current state.
        var ingests = (run.Steps ?? [])
            .Select(static step => TryReadIngest(step.Output))
            .OfType<TestResultIngestOutput>()
            .ToArray();

        return ingests
            .SelectMany(ingest => ingest.Cases.Select(@case => new TraceabilityRow(
                RequirementProvider: requirement is null ? null : "github",
                RequirementId: requirement?.ResourceId,
                RequirementUrl: requirement?.ResourceUrl,
                TestId: @case.Id,
                TestName: @case.Name,
                CiRunId: ExtractCiRunId(ingest.CiRunUrl),
                CiRunUrl: ingest.CiRunUrl,
                Status: @case.Status,
                EvidenceArtifact: ingest.ReportArtifact,
                FailureMessage: @case.FailureMessage ?? @case.FailureType)))
            .ToArray();
    }

    /// <summary>
    /// The trailing segment of a GitHub Actions run URL (".../actions/runs/123"). Best-effort and
    /// null when the URL does not look like that: the URL itself is the authoritative link, and a
    /// wrong id parsed out of an unfamiliar CI system's URL would be worse than none.
    /// </summary>
    private static string? ExtractCiRunId(string? ciRunUrl)
    {
        if (string.IsNullOrWhiteSpace(ciRunUrl) || !Uri.TryCreate(ciRunUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.Segments
            .Select(static segment => segment.Trim('/'))
            .Where(static segment => segment.Length > 0)
            .ToArray();

        var runsIndex = Array.FindLastIndex(segments, static segment =>
            string.Equals(segment, "runs", StringComparison.OrdinalIgnoreCase));

        return runsIndex >= 0 && runsIndex < segments.Length - 1
            ? segments[runsIndex + 1]
            : null;
    }

    private static TestResultIngestOutput? TryReadIngest(string? output)
    {
        if (string.IsNullOrWhiteSpace(output) || !output.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<TestResultIngestOutput>(output);

            // Every step's output lands here, most of them not ingests. Only claim the ones that say
            // they are, so a coincidentally shaped output cannot masquerade as test results.
            return parsed is not null
                && string.Equals(parsed.Action, "ingest_test_results", StringComparison.Ordinal)
                && parsed.Cases is not null
                    ? parsed
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ExternalActionEvent? TryReadAction(WorkflowEvent @event)
    {
        try
        {
            return JsonSerializer.Deserialize<ExternalActionEvent>(@event.Message, EventJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Mirrors the payload WorkflowInstanceEngine writes for "external_action_recorded". Only the
    /// fields a row needs are read; the rest of the event is left alone.
    /// </summary>
    private sealed record ExternalActionEvent(
        string? Provider,
        string? Action,
        string? Status,
        string? ResourceId,
        string? ResourceUrl);
}

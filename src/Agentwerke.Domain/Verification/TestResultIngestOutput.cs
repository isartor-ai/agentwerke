using System.Text.Json.Serialization;

namespace Agentwerke.Domain.Verification;

/// <summary>
/// The structured output of a test-result ingestion step (#210).
/// </summary>
/// <remarks>
/// A shared type rather than an anonymous object, because this crosses a module boundary: the
/// ingest tool writes it into the step's output, and the traceability row reads it back out. Left as
/// two hand-written JSON shapes, a renamed field would break the row silently at runtime and the
/// row would simply show nothing. Here it does not compile.
/// </remarks>
public sealed record TestResultIngestOutput(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("format")] string Format,
    /// <summary>"passed" or "failed" — the CI outcome, not whether the ingestion worked.</summary>
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("passed")] int Passed,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("errors")] int Errors,
    [property: JsonPropertyName("skipped")] int Skipped,
    [property: JsonPropertyName("duration_ms")] double? DurationMs,
    [property: JsonPropertyName("incomplete")] bool Incomplete,
    [property: JsonPropertyName("declared_total")] int? DeclaredTotal,
    [property: JsonPropertyName("ci_run_url")] string? CiRunUrl,
    [property: JsonPropertyName("report_url")] string? ReportUrl,
    [property: JsonPropertyName("report_artifact")] string ReportArtifact,
    [property: JsonPropertyName("cases")] IReadOnlyList<TestResultIngestCase> Cases);

public sealed record TestResultIngestCase(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("class_name")] string? ClassName,
    [property: JsonPropertyName("suite")] string? Suite,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("duration_ms")] double? DurationMs,
    [property: JsonPropertyName("failure_type")] string? FailureType,
    [property: JsonPropertyName("failure_message")] string? FailureMessage,
    [property: JsonPropertyName("failure_detail")] string? FailureDetail);

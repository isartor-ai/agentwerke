namespace Agentwerke.Domain.Verification;

public enum TestCaseStatus
{
    Passed,
    Failed,
    Error,
    Skipped
}

/// <summary>
/// Why a test case did not pass. Kept separate from the case so that "there is a failure" and "the
/// failure said nothing useful" are distinguishable — a &lt;failure/&gt; with no message is common and
/// must not be mistaken for a pass.
/// </summary>
public sealed record TestFailureDetail(
    string? Type,
    string? Message,
    string? Detail);

/// <summary>
/// One executed test case. <see cref="Id"/> is the identity a traceability row links to (#210), so it
/// has to be stable across runs: it is "classname.name" where a class name exists, which is what every
/// JUnit-emitting runner keys a test on, and bare "name" otherwise.
/// </summary>
public sealed record TestCaseResult(
    string Id,
    string Name,
    string? ClassName,
    string? SuiteName,
    TestCaseStatus Status,
    double? DurationMs,
    TestFailureDetail? Failure);

/// <summary>
/// A parsed test run.
/// </summary>
/// <remarks>
/// The counts are derived from the cases rather than read from the report's own
/// <c>tests</c>/<c>failures</c> attributes. Those attributes are self-reported, disagree with the
/// case list in several runners, and would let a report claim "0 failures" over a body that contains
/// failures. Evidence has to describe what is actually in the document (#210).
/// <see cref="DeclaredTotal"/> keeps the claim for comparison rather than discarding it.
/// </remarks>
public sealed record TestRunResult(
    IReadOnlyList<TestCaseResult> Cases,
    double? DurationMs = null,
    int? DeclaredTotal = null)
{
    public int Total => Cases.Count;

    public int Passed => Cases.Count(static c => c.Status == TestCaseStatus.Passed);

    public int Failed => Cases.Count(static c => c.Status == TestCaseStatus.Failed);

    public int Errors => Cases.Count(static c => c.Status == TestCaseStatus.Error);

    public int Skipped => Cases.Count(static c => c.Status == TestCaseStatus.Skipped);

    /// <summary>
    /// A run is green only if nothing failed or errored. Skips do not fail it — but they do not
    /// verify anything either, so a caller asserting a requirement was verified should check
    /// <see cref="Passed"/> rather than this.
    /// </summary>
    public bool Succeeded => Failed == 0 && Errors == 0;

    /// <summary>
    /// True when the report's own case count disagrees with the cases actually present — a truncated
    /// or partially written artifact. The result is still usable; the caller decides whether to trust
    /// it, and should not silently treat it as complete.
    /// </summary>
    public bool IsIncomplete => DeclaredTotal is { } declared && declared != Cases.Count;
}

public sealed class TestResultParseException : Exception
{
    public TestResultParseException(string message)
        : base(message)
    {
    }

    public TestResultParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

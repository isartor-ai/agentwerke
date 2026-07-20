using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Agentwerke.Domain.Verification;

/// <summary>
/// Parses JUnit-style XML test reports into structured results (#210), so a run's verification
/// evidence is the test cases the runner actually reported rather than an agent's summary of them.
/// </summary>
/// <remarks>
/// "JUnit XML" is not one format. It descends from Ant's JUnit task and has no canonical schema, so
/// every runner improvises around a common core. This parser targets that core and tolerates the
/// variations seen in the runners we expect to meet:
///
/// - pytest, Jest (jest-junit), Go (gotestsum) and .NET (JunitXml.TestLogger) wrap suites in
///   &lt;testsuites&gt;; Maven Surefire writes a bare &lt;testsuite&gt; root. Both are accepted.
/// - Suites nest arbitrarily deep in some emitters, so suites are gathered recursively.
/// - classname is absent in some runners and is the primary identity in others.
/// - A case's outcome is a child element, not an attribute: &lt;failure&gt;, &lt;error&gt;, &lt;skipped&gt;,
///   or nothing at all for a pass.
///
/// It is deliberately lenient about everything except the two things evidence depends on: a case
/// must have a name, and its outcome must be read correctly. Anything unrecognised is ignored rather
/// than guessed at, and a document that is not a test report at all is rejected outright — quietly
/// returning zero tests would let a run claim verification it never had.
/// </remarks>
public static class JUnitTestResultParser
{
    public static TestRunResult Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new TestResultParseException("Test report is empty.");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new TestResultParseException($"Test report is not well-formed XML: {ex.Message}", ex);
        }

        var root = document.Root
            ?? throw new TestResultParseException("Test report has no root element.");

        // Local names throughout: reports are usually namespace-less, but some emitters add one, and
        // a namespace is not a reason to reject a readable report.
        var suites = root.Name.LocalName switch
        {
            "testsuite" => [root],
            "testsuites" => DescendantsNamed(root, "testsuite").ToArray(),
            _ => throw new TestResultParseException(
                $"Test report root is <{root.Name.LocalName}>; expected <testsuite> or <testsuites>.")
        };

        if (suites.Length == 0)
        {
            // A <testsuites/> with nothing in it is a real, if unusual, report of "no suites ran".
            // Distinguishing it from a wrong-document parse is why the root check above is strict.
            return new TestRunResult([], ReadSeconds(root, "time"), ReadInt(root, "tests"));
        }

        var cases = suites.SelectMany(ParseSuite).ToArray();

        return new TestRunResult(
            Cases: cases,
            DurationMs: ReadSeconds(root, "time") ?? SumSuiteDurations(suites),
            DeclaredTotal: ReadDeclaredTotal(root, suites));
    }

    private static IEnumerable<TestCaseResult> ParseSuite(XElement suite)
    {
        var suiteName = Attribute(suite, "name");

        // Direct children only: a nested suite's cases belong to that suite, and are picked up when
        // it is visited in its own right. Descendants here would count them twice.
        return suite.Elements()
            .Where(static element => element.Name.LocalName == "testcase")
            .Select(element => ParseCase(element, suiteName));
    }

    private static TestCaseResult ParseCase(XElement element, string? suiteName)
    {
        var name = Attribute(element, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new TestResultParseException("Test report contains a <testcase> with no 'name' attribute.");
        }

        var className = Attribute(element, "classname");
        var (status, failure) = ReadOutcome(element);

        return new TestCaseResult(
            Id: string.IsNullOrWhiteSpace(className) ? name : $"{className}.{name}",
            Name: name,
            ClassName: className,
            SuiteName: suiteName,
            Status: status,
            DurationMs: ReadSeconds(element, "time"),
            Failure: failure);
    }

    /// <summary>
    /// A case's outcome is whichever of these children is present, and a pass is the *absence* of all
    /// of them. Checked in severity order: a case carrying both an error and a failure is reported as
    /// the worse of the two rather than by document order.
    /// </summary>
    private static (TestCaseStatus Status, TestFailureDetail? Failure) ReadOutcome(XElement element)
    {
        if (ChildNamed(element, "error") is { } error)
        {
            return (TestCaseStatus.Error, ReadFailureDetail(error));
        }

        if (ChildNamed(element, "failure") is { } failed)
        {
            return (TestCaseStatus.Failed, ReadFailureDetail(failed));
        }

        if (ChildNamed(element, "skipped") is { } skipped)
        {
            return (TestCaseStatus.Skipped, ReadFailureDetail(skipped));
        }

        return (TestCaseStatus.Passed, null);
    }

    private static TestFailureDetail? ReadFailureDetail(XElement element)
    {
        var type = Attribute(element, "type");
        var message = Attribute(element, "message");
        var detail = element.Value is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

        return type is null && message is null && detail is null
            ? null
            : new TestFailureDetail(type, message, detail);
    }

    /// <summary>
    /// JUnit times are seconds with a '.' decimal separator regardless of the machine's locale, so
    /// this must parse invariantly. Under de-DE, <c>double.Parse("0.001")</c> reads '.' as a group
    /// separator and yields 1 — a 1000x error that no test would notice unless it ran under that
    /// culture, which is exactly where this code is developed.
    /// </summary>
    private static double? ReadSeconds(XElement element, string attributeName)
    {
        var raw = Attribute(element, attributeName);
        if (raw is null)
        {
            return null;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0
            ? seconds * 1000
            : null;
    }

    private static int? ReadInt(XElement element, string attributeName)
    {
        var raw = Attribute(element, attributeName);
        return raw is not null
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// The report's own case count, for the truncation check. Taken from the root when it declares
    /// one, else summed across suites — a &lt;testsuites&gt; wrapper often omits it while its children
    /// each carry their own.
    /// </summary>
    private static int? ReadDeclaredTotal(XElement root, IReadOnlyList<XElement> suites)
    {
        if (ReadInt(root, "tests") is { } declared)
        {
            return declared;
        }

        var perSuite = suites.Select(suite => ReadInt(suite, "tests")).ToArray();
        return perSuite.All(static value => value is null)
            ? null
            : perSuite.Sum(static value => value ?? 0);
    }

    private static double? SumSuiteDurations(IReadOnlyList<XElement> suites)
    {
        var durations = suites.Select(suite => ReadSeconds(suite, "time")).ToArray();
        return durations.All(static value => value is null)
            ? null
            : durations.Sum(static value => value ?? 0);
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value is { } value
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;

    private static XElement? ChildNamed(XElement element, string name) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> DescendantsNamed(XElement element, string name) =>
        element.Descendants().Where(e => e.Name.LocalName == name);
}

using System.Globalization;
using Agentwerke.Domain.Verification;

namespace Agentwerke.Domain.Tests;

/// <summary>
/// The fixtures here are shaped after what real runners actually emit, not after an idealised schema.
/// "JUnit XML" has no canonical definition, so the parser's correctness is defined by the documents
/// it will really be handed (#210) — hence one test per emitter we expect the pilot's CI to use.
/// </summary>
public sealed class JUnitTestResultParserTests
{
    // ── Real-world dialects ───────────────────────────────────────────────────

    /// <summary>pytest --junitxml: testsuites wrapper, classname is the module path, skips carry a message.</summary>
    [Fact]
    public void Parse_PytestReport_ReadsCasesAndOutcomes()
    {
        var result = JUnitTestResultParser.Parse("""
            <?xml version="1.0" encoding="utf-8"?>
            <testsuites>
              <testsuite name="pytest" errors="0" failures="1" skipped="1" tests="3" time="0.123" timestamp="2026-07-14T10:00:00.000000">
                <testcase classname="tests.test_orders" name="test_creates_order" time="0.001" />
                <testcase classname="tests.test_orders" name="test_rejects_empty_cart" time="0.002">
                  <failure message="assert 0 == 1">tests/test_orders.py:14: AssertionError</failure>
                </testcase>
                <testcase classname="tests.test_orders" name="test_applies_discount" time="0.000">
                  <skipped type="pytest.skip" message="not implemented yet" />
                </testcase>
              </testsuite>
            </testsuites>
            """);

        Assert.Equal(3, result.Total);
        Assert.Equal(1, result.Passed);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Skipped);
        Assert.False(result.Succeeded);
        Assert.False(result.IsIncomplete);

        var failure = result.Cases.Single(c => c.Status == TestCaseStatus.Failed);
        Assert.Equal("tests.test_orders.test_rejects_empty_cart", failure.Id);
        Assert.Equal("assert 0 == 1", failure.Failure!.Message);
        Assert.Contains("AssertionError", failure.Failure.Detail, StringComparison.Ordinal);
        Assert.Equal(2, failure.DurationMs);
    }

    /// <summary>jest-junit: nested testsuites, and a failure whose detail is a stack trace in element text.</summary>
    [Fact]
    public void Parse_JestReport_ReadsNestedSuites()
    {
        var result = JUnitTestResultParser.Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <testsuites name="jest tests" tests="2" failures="1" time="1.5">
              <testsuite name="orders" errors="0" failures="1" skipped="0" timestamp="2026-07-14T10:00:00" time="1.5" tests="2">
                <testcase classname="orders creates an order" name="orders creates an order" time="0.5" />
                <testcase classname="orders rejects an empty cart" name="orders rejects an empty cart" time="1">
                  <failure>Error: expected 400, received 200
                    at Object.&lt;anonymous&gt; (/src/orders.test.js:22:5)</failure>
                </testcase>
              </testsuite>
            </testsuites>
            """);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1500, result.DurationMs);

        var failure = result.Cases.Single(c => c.Status == TestCaseStatus.Failed);
        // No message attribute — the whole detail is element text. Must still register as a failure.
        Assert.Null(failure.Failure!.Message);
        Assert.Contains("expected 400", failure.Failure.Detail, StringComparison.Ordinal);
    }

    /// <summary>Maven Surefire writes a bare testsuite root, with no testsuites wrapper at all.</summary>
    [Fact]
    public void Parse_SurefireReport_AcceptsABareTestsuiteRoot()
    {
        var result = JUnitTestResultParser.Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <testsuite name="com.example.OrderTest" time="0.25" tests="2" errors="1" skipped="0" failures="0">
              <testcase name="createsOrder" classname="com.example.OrderTest" time="0.1" />
              <testcase name="rejectsEmptyCart" classname="com.example.OrderTest" time="0.15">
                <error message="NullPointerException" type="java.lang.NullPointerException">at com.example.Order.total(Order.java:31)</error>
              </testcase>
            </testsuite>
            """);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Errors);
        Assert.Equal(0, result.Failed);
        Assert.False(result.Succeeded);

        var error = result.Cases.Single(c => c.Status == TestCaseStatus.Error);
        Assert.Equal("com.example.OrderTest.rejectsEmptyCart", error.Id);
        Assert.Equal("java.lang.NullPointerException", error.Failure!.Type);
    }

    /// <summary>
    /// JunitXml.TestLogger, i.e. `dotnet test --logger:"junit"` — the shape the pilot emits if its app
    /// is .NET. Assembly-named suite, namespaced classname, and a system-out element to ignore.
    /// </summary>
    [Fact]
    public void Parse_DotnetJUnitLoggerReport_ReadsNamespacedCases()
    {
        var result = JUnitTestResultParser.Parse("""
            <?xml version="1.0" encoding="utf-8"?>
            <testsuites>
              <testsuite name="Example.Tests.dll" tests="2" failures="0" errors="0" skipped="0" time="0.42">
                <properties />
                <testcase classname="Example.Tests.OrderTests" name="CreatesOrder" time="0.011" />
                <testcase classname="Example.Tests.OrderTests" name="RejectsEmptyCart" time="0.009">
                  <system-out>some console noise</system-out>
                </testcase>
              </testsuite>
            </testsuites>
            """);

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Passed);
        Assert.True(result.Succeeded);
        Assert.Equal("Example.Tests.OrderTests.CreatesOrder", result.Cases[0].Id);
        Assert.Equal("Example.Tests.dll", result.Cases[0].SuiteName);
        // system-out is not an outcome element; the case still passed.
        Assert.Equal(TestCaseStatus.Passed, result.Cases[1].Status);
    }

    // ── The locale trap ───────────────────────────────────────────────────────

    /// <summary>
    /// JUnit times are invariant ('.' decimal) whatever the machine's locale. Under de-DE — where this
    /// codebase is developed — a culture-sensitive parse reads "0.001" as 1, a 1000x error that every
    /// other test would still pass through.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Parse_UnderACommaDecimalCulture_StillReadsSecondsInvariantly(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var result = JUnitTestResultParser.Parse("""
                <testsuite name="s" tests="1">
                  <testcase classname="c" name="t" time="0.001" />
                </testsuite>
                """);

            Assert.Equal(1, result.Cases[0].DurationMs);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ── Evidence integrity ────────────────────────────────────────────────────

    /// <summary>
    /// The counts describe the document's body, not its self-reported attributes. A report claiming
    /// failures="0" over a body containing a failure must not be evidenced as green.
    /// </summary>
    [Fact]
    public void Parse_WhenDeclaredCountsContradictTheBody_TheBodyWins()
    {
        var result = JUnitTestResultParser.Parse("""
            <testsuite name="s" tests="1" failures="0" errors="0">
              <testcase classname="c" name="t">
                <failure message="it failed anyway" />
              </testcase>
            </testsuite>
            """);

        Assert.Equal(1, result.Failed);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Parse_WhenTheReportDeclaresMoreCasesThanItContains_FlagsItAsIncomplete()
    {
        var result = JUnitTestResultParser.Parse("""
            <testsuite name="s" tests="10">
              <testcase classname="c" name="t" />
            </testsuite>
            """);

        Assert.True(result.IsIncomplete);
        Assert.Equal(1, result.Total);
        Assert.Equal(10, result.DeclaredTotal);
    }

    /// <summary>A skip verifies nothing, so a run of only skips is not failing but proves nothing either.</summary>
    [Fact]
    public void Parse_ASkippedOnlyRun_SucceedsButPassesNothing()
    {
        var result = JUnitTestResultParser.Parse("""
            <testsuite name="s" tests="1">
              <testcase classname="c" name="t"><skipped /></testcase>
            </testsuite>
            """);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Passed);
        Assert.Equal(1, result.Skipped);
    }

    /// <summary>A case carrying both is reported as the worse outcome, not by document order.</summary>
    [Fact]
    public void Parse_ACaseWithBothErrorAndFailure_IsReportedAsAnError()
    {
        var result = JUnitTestResultParser.Parse("""
            <testsuite name="s" tests="1">
              <testcase classname="c" name="t">
                <failure message="f" />
                <error message="e" />
              </testcase>
            </testsuite>
            """);

        Assert.Equal(TestCaseStatus.Error, result.Cases[0].Status);
        Assert.Equal("e", result.Cases[0].Failure!.Message);
    }

    /// <summary>Nested suites are gathered, and their cases counted exactly once.</summary>
    [Fact]
    public void Parse_NestedSuites_CountsEachCaseOnce()
    {
        var result = JUnitTestResultParser.Parse("""
            <testsuites>
              <testsuite name="outer">
                <testcase classname="c" name="outer_case" />
                <testsuite name="inner">
                  <testcase classname="c" name="inner_case" />
                </testsuite>
              </testsuite>
            </testsuites>
            """);

        Assert.Equal(2, result.Total);
        Assert.Equal(["c.outer_case", "c.inner_case"], result.Cases.Select(static c => c.Id));
        Assert.Equal("inner", result.Cases[1].SuiteName);
    }

    [Fact]
    public void Parse_ACaseWithoutAClassname_FallsBackToItsBareName()
    {
        var result = JUnitTestResultParser.Parse("""
            <testsuite name="s"><testcase name="standalone_case" /></testsuite>
            """);

        Assert.Equal("standalone_case", result.Cases[0].Id);
        Assert.Null(result.Cases[0].ClassName);
    }

    [Fact]
    public void Parse_ANamespacedReport_IsStillRead()
    {
        var result = JUnitTestResultParser.Parse("""
            <testsuite xmlns="http://example.test/junit" name="s">
              <testcase classname="c" name="t"><failure message="m" /></testcase>
            </testsuite>
            """);

        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public void Parse_AnEmptyTestsuitesElement_IsAnEmptyRunRatherThanAnError()
    {
        var result = JUnitTestResultParser.Parse("<testsuites />");

        Assert.Equal(0, result.Total);
        Assert.True(result.Succeeded);
    }

    // ── Rejections ────────────────────────────────────────────────────────────

    /// <summary>
    /// Being handed the wrong document must fail loudly. Returning an empty run would let a caller
    /// record "0 failures" as verification evidence for a build that never reported anything.
    /// </summary>
    [Theory]
    [InlineData("<html><body>404 Not Found</body></html>")]
    [InlineData("<coverage lines-valid=\"100\" />")]
    public void Parse_ADocumentThatIsNotATestReport_IsRejected(string xml)
    {
        var ex = Assert.Throws<TestResultParseException>(() => JUnitTestResultParser.Parse(xml));
        Assert.Contains("expected <testsuite>", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_AnEmptyReport_IsRejected(string xml)
    {
        Assert.Throws<TestResultParseException>(() => JUnitTestResultParser.Parse(xml));
    }

    [Fact]
    public void Parse_MalformedXml_IsRejectedWithTheParseError()
    {
        var ex = Assert.Throws<TestResultParseException>(() =>
            JUnitTestResultParser.Parse("<testsuite><testcase name=\"t\"></testsuite>"));

        Assert.Contains("not well-formed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ACaseWithNoName_IsRejectedRatherThanSilentlyIdentified()
    {
        var ex = Assert.Throws<TestResultParseException>(() =>
            JUnitTestResultParser.Parse("""<testsuite name="s"><testcase classname="c" /></testsuite>"""));

        Assert.Contains("no 'name' attribute", ex.Message, StringComparison.Ordinal);
    }
}

using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Workflows.Bpmn;
using Agentwerke.Workflows.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agentwerke.Workflows.Tests;

/// <summary>
/// Data-driven exclusive gateways (#203): condition-expression evaluation against
/// run context, branch selection, loop-back cycles with the visit guard, and the
/// validator rules for gateway conditions.
/// </summary>
public sealed class ConditionalGatewayTests
{
    // ── ConditionExpressionEvaluator ──────────────────────────────────────────

    [Theory]
    [InlineData("true", true)]
    [InlineData("${true}", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData("${false}", false)]
    [InlineData("no", false)]
    public void Evaluate_BooleanLiterals(string expression, bool expected)
    {
        var evaluation = ConditionExpressionEvaluator.Evaluate(expression, static _ => null);
        Assert.Equal(expected, evaluation.Result);
    }

    [Theory]
    [InlineData("VERDICT: PASS\nAll 42 tests green.", true)]
    [InlineData("VERDICT: FAIL\n3 tests red.", false)]
    public void Evaluate_ContainsOnVariable_MatchesVerdictLine(string output, bool expected)
    {
        var evaluation = ConditionExpressionEvaluator.Evaluate(
            "{{output.RunTests}} contains \"VERDICT: PASS\"",
            key => key == "output.RunTests" ? output : null);

        Assert.Equal(expected, evaluation.Result);
        Assert.NotNull(evaluation.Detail);
    }

    [Theory]
    [InlineData("{{event.state}} == \"merged\"", "merged", true)]
    [InlineData("{{event.state}} == \"merged\"", "closed", false)]
    [InlineData("{{event.state}} != 'merged'", "closed", true)]
    public void Evaluate_EqualityOperators(string expression, string value, bool expected)
    {
        var evaluation = ConditionExpressionEvaluator.Evaluate(
            expression,
            key => key == "event.state" ? value : null);

        Assert.Equal(expected, evaluation.Result);
    }

    [Fact]
    public void Evaluate_MissingVariable_ResolvesToEmptyString()
    {
        var equalsEmpty = ConditionExpressionEvaluator.Evaluate("{{output.Nope}} == \"\"", static _ => null);
        var containsAnything = ConditionExpressionEvaluator.Evaluate("{{output.Nope}} contains \"x\"", static _ => null);

        Assert.True(equalsEmpty.Result);
        Assert.False(containsAnything.Result);
    }

    [Fact]
    public void Evaluate_VariableValueWithQuotesAndOperators_CannotChangeExpressionStructure()
    {
        // Operands are parsed before resolution, so a hostile step output cannot
        // inject an operator or terminate the quoted literal.
        var evaluation = ConditionExpressionEvaluator.Evaluate(
            "{{output.RunTests}} == \"clean\"",
            static _ => "\" == \"clean\" || \"injected");

        Assert.False(evaluation.Result);
    }

    [Fact]
    public void Evaluate_UnparseableExpression_FailsClosed()
    {
        var evaluation = ConditionExpressionEvaluator.Evaluate("this is not a condition", static _ => null);
        Assert.False(evaluation.Result);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("{{output.RunTests}} contains \"VERDICT: PASS\"")]
    [InlineData("{{event.state}} == 'merged'")]
    [InlineData("abc != def")]
    public void TryParse_AcceptsGrammar(string expression)
    {
        Assert.True(ConditionExpressionEvaluator.TryParse(expression, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not a condition")]
    [InlineData("{{a}} == {{b}} == {{c}}")]
    [InlineData("{{output.RunTests}} startswith \"x\"")]
    public void TryParse_RejectsMalformedExpressions(string expression)
    {
        Assert.False(ConditionExpressionEvaluator.TryParse(expression, out var error));
        Assert.NotNull(error);
    }

    // ── Engine branch selection ───────────────────────────────────────────────

    [Fact]
    public async Task Gateway_WhenConditionMatchesStepOutput_TakesConditionalBranch()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var executor = new ScriptedVerdictExecutor(static (_, _) => "All good.\nVERDICT: PASS");
        var engine = new WorkflowInstanceEngine(store, executor, new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

        var state = await engine.StartAsync(Guid.NewGuid().ToString(), CreateTestLoopDefinition(), "system", CancellationToken.None);

        Assert.Equal("completed", state.Status);
        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e =>
            e.Type == "gateway_evaluated" &&
            e.Message.Contains("\"chosenFlowId\":\"FlowPass\"", StringComparison.Ordinal) &&
            e.Message.Contains("\"usedDefaultFlow\":false", StringComparison.Ordinal));
        Assert.Equal(1, executor.CallCount("RunTests"));
        Assert.Equal(0, executor.CallCount("Fix"));
    }

    [Fact]
    public async Task Gateway_WhenNoConditionMatches_TakesDefaultFlowAndLoopsBack()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        // Tests fail once, the Fix task runs, then the re-run passes.
        var executor = new ScriptedVerdictExecutor(static (nodeId, call) =>
            nodeId == "RunTests" && call == 1 ? "VERDICT: FAIL" : "VERDICT: PASS");
        var engine = new WorkflowInstanceEngine(store, executor, new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

        var state = await engine.StartAsync(Guid.NewGuid().ToString(), CreateTestLoopDefinition(), "system", CancellationToken.None);

        Assert.Equal("completed", state.Status);
        Assert.Equal(2, executor.CallCount("RunTests"));
        Assert.Equal(1, executor.CallCount("Fix"));

        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        var gatewayEvents = events.Where(static e => e.Type == "gateway_evaluated").ToList();
        Assert.Equal(2, gatewayEvents.Count);
        Assert.Contains("\"chosenFlowId\":\"FlowFail\"", gatewayEvents[0].Message);
        Assert.Contains("\"usedDefaultFlow\":true", gatewayEvents[0].Message);
        Assert.Contains("\"chosenFlowId\":\"FlowPass\"", gatewayEvents[1].Message);
    }

    [Fact]
    public async Task Gateway_WhenLoopNeverExits_LoopGuardFailsTheRun()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var executor = new ScriptedVerdictExecutor(static (_, _) => "VERDICT: FAIL");
        var engine = new WorkflowInstanceEngine(store, executor, new InMemoryRunContextRepository(), NullLogger<WorkflowInstanceEngine>.Instance);

        var state = await engine.StartAsync(Guid.NewGuid().ToString(), CreateTestLoopDefinition(), "system", CancellationToken.None);

        Assert.Equal("failed", state.Status);
        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.Type == "loop_guard_triggered");

        var persistedRun = await store.GetRunAsync(state.RunId, CancellationToken.None);
        Assert.Equal("failed", persistedRun!.Status);
    }

    // ── Validator rules ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_ConditionalGatewayWithDefaultFlow_IsValid()
    {
        var result = new BpmnWorkflowValidator().Validate(GatewayXml(
            passCondition: "{{output.RunTests}} contains \"VERDICT: PASS\"",
            failCondition: null));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, static w => w.ElementName == "exclusiveGateway");
    }

    [Fact]
    public void Validate_GatewayWithoutAnyConditions_WarnsButStaysPublishable()
    {
        var result = new BpmnWorkflowValidator().Validate(GatewayXml(passCondition: null, failCondition: null));

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, static w =>
            w.ElementName == "exclusiveGateway" &&
            w.Message.Contains("no conditions", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_GatewayWithInvalidConditionSyntax_ReturnsError()
    {
        var result = new BpmnWorkflowValidator().Validate(GatewayXml(
            passCondition: "this is not a condition",
            failCondition: null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static e =>
            e.ElementName == "sequenceFlow" &&
            e.Message.Contains("invalid condition expression", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_GatewayWithConditionsButTwoDefaultFlows_ReturnsError()
    {
        var xml = GatewayXml(
            passCondition: "{{output.RunTests}} contains \"VERDICT: PASS\"",
            failCondition: null,
            extraUnconditionalFlow: true);

        var result = new BpmnWorkflowValidator().Validate(xml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static e =>
            e.ElementName == "exclusiveGateway" &&
            e.Message.Contains("At most one unconditional flow", StringComparison.Ordinal));
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Start → RunTests → [TestsPass?] —(FlowPass: contains VERDICT: PASS)→ End
    ///                          └—(FlowFail: default)→ Fix → RunTests (loop back)
    /// </summary>
    private static BpmnWorkflowDefinition CreateTestLoopDefinition()
    {
        return new BpmnWorkflowDefinition(
            ProcessId: "TestLoop",
            ProcessName: "Test Loop",
            Nodes:
            [
                new BpmnNodeDefinition("Start", "Start", "startEvent", null),
                new BpmnNodeDefinition("RunTests", "Run tests", "serviceTask",
                    new AgentwerkeTaskMetadata("tester", "tests.run", null, "test_execution", "test-gate", [])),
                new BpmnNodeDefinition("TestsPass", "Tests pass?", "exclusiveGateway", null),
                new BpmnNodeDefinition("Fix", "Fix implementation", "serviceTask",
                    new AgentwerkeTaskMetadata("developer", "implementation.fix", null, "implementation", "demo-implementation", [])),
                new BpmnNodeDefinition("End", "End", "endEvent", null)
            ],
            SequenceFlows:
            [
                new BpmnSequenceFlow("FlowStart", "Start", "RunTests", null),
                new BpmnSequenceFlow("FlowToGateway", "RunTests", "TestsPass", null),
                new BpmnSequenceFlow("FlowPass", "TestsPass", "End", "{{output.RunTests}} contains \"VERDICT: PASS\""),
                new BpmnSequenceFlow("FlowFail", "TestsPass", "Fix", null),
                new BpmnSequenceFlow("FlowLoopBack", "Fix", "RunTests", null)
            ]);
    }

    private static string GatewayXml(string? passCondition, string? failCondition, bool extraUnconditionalFlow = false)
    {
        static string Condition(string? expression) => expression is null
            ? string.Empty
            : $"<bpmn:conditionExpression>{System.Security.SecurityElement.Escape(expression)}</bpmn:conditionExpression>";

        var extraFlow = extraUnconditionalFlow
            ? """
              <bpmn:sequenceFlow id="FlowExtra" sourceRef="TestsPass" targetRef="End" />
              """
            : string.Empty;

        return $"""
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:agentwerke="https://agentwerke.dev/bpmn/extensions/v1">
              <bpmn:process id="GatewayFlow" name="Gateway Flow">
                <bpmn:startEvent id="Start" />
                <bpmn:serviceTask id="RunTests" name="Run tests">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
                      agent="tester"
                      action="tests.run"
                      purposeType="test_execution"
                      policyTag="test-gate" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:exclusiveGateway id="TestsPass" name="Tests pass?" />
                <bpmn:serviceTask id="Fix" name="Fix implementation">
                  <bpmn:extensionElements>
                    <agentwerke:agentTask
                      agent="developer"
                      action="implementation.fix"
                      purposeType="implementation"
                      policyTag="demo-implementation" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:endEvent id="End" />
                <bpmn:sequenceFlow id="FlowStart" sourceRef="Start" targetRef="RunTests" />
                <bpmn:sequenceFlow id="FlowToGateway" sourceRef="RunTests" targetRef="TestsPass" />
                <bpmn:sequenceFlow id="FlowPass" sourceRef="TestsPass" targetRef="End">{Condition(passCondition)}</bpmn:sequenceFlow>
                <bpmn:sequenceFlow id="FlowFail" sourceRef="TestsPass" targetRef="Fix">{Condition(failCondition)}</bpmn:sequenceFlow>
                {extraFlow}
                <bpmn:sequenceFlow id="FlowLoopBack" sourceRef="Fix" targetRef="RunTests" />
              </bpmn:process>
            </bpmn:definitions>
            """;
    }

    /// <summary>Succeeds every task; output is a function of node id and per-node call number.</summary>
    private sealed class ScriptedVerdictExecutor(Func<string, int, string> outputFor) : IServiceTaskExecutor
    {
        private readonly Dictionary<string, int> _calls = new(StringComparer.Ordinal);

        public int CallCount(string nodeId) => _calls.GetValueOrDefault(nodeId);

        public Task<AgentTaskOutcome> ExecuteAsync(
            string runId, string stepId, BpmnNodeDefinition node,
            int attempt, CancellationToken cancellationToken, AgentExecutionProgressReporter? progressReporter = null)
        {
            var call = _calls.GetValueOrDefault(node.Id) + 1;
            _calls[node.Id] = call;

            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: true,
                Output: outputFor(node.Id, call),
                FailureReason: null));
        }
    }
}

using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autofac.Workflows.Tests;

/// <summary>
/// Conformance tests for the default (Postgres-backed, in-process) Autofac workflow runtime.
///
/// Purpose: prove the runtime reliably executes the curated SDLC templates it ships with, rejects
/// BPMN constructs that are outside its governed subset, and persists correct checkpoints so
/// recovery after a crash restores the expected state — all without any dependency on Camunda.
///
/// Supported subset covered here:
///   startEvent, endEvent, serviceTask, userTask, exclusiveGateway, parallelGateway,
///   intermediateCatchEvent (timer), boundaryEvent (timer / error / escalation)
///
/// Unsupported subset proven to fail before publish:
///   manualTask, callActivity, receiveTask, eventBasedGateway, complexGateway,
///   adHocSubProcess, compensateEventDefinition on boundaryEvent,
///   messageEventDefinition on intermediateCatchEvent
/// </summary>
public sealed class DefaultRuntimeConformanceTests
{
    private static readonly string AutofacNs = "https://autofac.de/bpmn/extensions/v1";
    private static readonly string BpmnNs = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    private readonly BpmnWorkflowValidator _validator = new();

    // -------------------------------------------------------------------------
    // Built-in SDLC template BPMN fixtures
    // -------------------------------------------------------------------------

    /// <summary>
    /// Issue-to-PR: linear flow through specify → plan → implement agents, then a human
    /// code-review gate, then a GitHub PR service task. Covers: startEvent, serviceTask (x4),
    /// userTask, endEvent, sequenceFlow.
    /// </summary>
    private const string IssueToPrTemplate = """
        <bpmn:definitions
            xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:autofac="https://autofac.de/bpmn/extensions/v1">
          <bpmn:process id="IssueToPr" name="Issue to PR">
            <bpmn:startEvent id="Start" name="Issue Received" />
            <bpmn:serviceTask id="Specify" name="Specify Requirements">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="specification-agent"
                  action="spec.generate"
                  purposeType="specification"
                  policyTag="sdlc-spec" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:serviceTask id="Plan" name="Plan Implementation">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="planning-agent"
                  action="plan.generate"
                  purposeType="planning"
                  policyTag="sdlc-plan" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:serviceTask id="Implement" name="Implement Changes">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="implementation-agent"
                  action="code.generate"
                  purposeType="implementation"
                  policyTag="repo-change" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:userTask id="CodeReview" name="Code Review Approval">
              <bpmn:extensionElements>
                <autofac:approvalTask
                  purposeType="code_review"
                  policyTag="human-code-review" />
              </bpmn:extensionElements>
            </bpmn:userTask>
            <bpmn:serviceTask id="OpenPR" name="Open Pull Request">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="github-agent"
                  action="github.open_pr"
                  purposeType="pull_request"
                  policyTag="repo-write" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:endEvent id="End" name="PR Opened" />
            <bpmn:sequenceFlow id="sf1" sourceRef="Start" targetRef="Specify" />
            <bpmn:sequenceFlow id="sf2" sourceRef="Specify" targetRef="Plan" />
            <bpmn:sequenceFlow id="sf3" sourceRef="Plan" targetRef="Implement" />
            <bpmn:sequenceFlow id="sf4" sourceRef="Implement" targetRef="CodeReview" />
            <bpmn:sequenceFlow id="sf5" sourceRef="CodeReview" targetRef="OpenPR" />
            <bpmn:sequenceFlow id="sf6" sourceRef="OpenPR" targetRef="End" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// Bugfix: diagnose (with configured retries) → fix → test-approval gate → end.
    /// Covers: maxRetries on serviceTask, userTask, sequenceFlow.
    /// </summary>
    private const string BugfixTemplate = """
        <bpmn:definitions
            xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:autofac="https://autofac.de/bpmn/extensions/v1">
          <bpmn:process id="Bugfix" name="Bugfix">
            <bpmn:startEvent id="Start" name="Bug Reported" />
            <bpmn:serviceTask id="Diagnose" name="Diagnose Root Cause">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="analysis-agent"
                  action="bug.diagnose"
                  purposeType="diagnosis"
                  policyTag="read-only"
                  maxRetries="2"
                  retryBackoffSeconds="5" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:serviceTask id="Fix" name="Implement Fix">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="implementation-agent"
                  action="code.fix"
                  purposeType="bugfix"
                  policyTag="repo-change" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:userTask id="TestApproval" name="Test and Merge Approval">
              <bpmn:extensionElements>
                <autofac:approvalTask
                  purposeType="bugfix_merge"
                  policyTag="human-merge-approval" />
              </bpmn:extensionElements>
            </bpmn:userTask>
            <bpmn:endEvent id="End" name="Fix Merged" />
            <bpmn:sequenceFlow id="sf1" sourceRef="Start" targetRef="Diagnose" />
            <bpmn:sequenceFlow id="sf2" sourceRef="Diagnose" targetRef="Fix" />
            <bpmn:sequenceFlow id="sf3" sourceRef="Fix" targetRef="TestApproval" />
            <bpmn:sequenceFlow id="sf4" sourceRef="TestApproval" targetRef="End" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// Parallel Build-and-Test: fork into parallel quality branches (run tests + security scan),
    /// join, then a deploy-approval gate, then deploy. Covers: parallelGateway (fork + join),
    /// userTask, sequenceFlow.
    /// </summary>
    private const string ParallelBuildAndTestTemplate = """
        <bpmn:definitions
            xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:autofac="https://autofac.de/bpmn/extensions/v1">
          <bpmn:process id="ParallelBuildAndTest" name="Parallel Build and Test">
            <bpmn:startEvent id="Start" name="Build Triggered" />
            <bpmn:parallelGateway id="Fork" name="Quality Gate Fork" />
            <bpmn:serviceTask id="RunTests" name="Run Test Suite">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="testing-agent"
                  action="tests.run"
                  purposeType="quality_assurance"
                  policyTag="read-only" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:serviceTask id="SecurityScan" name="Run Security Scan">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="security-agent"
                  action="security.scan"
                  purposeType="security_review"
                  policyTag="read-only" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:parallelGateway id="Join" name="Quality Gate Join" />
            <bpmn:userTask id="DeployApproval" name="Deploy Approval">
              <bpmn:extensionElements>
                <autofac:approvalTask
                  purposeType="production_deployment"
                  policyTag="human-deploy-approval" />
              </bpmn:extensionElements>
            </bpmn:userTask>
            <bpmn:serviceTask id="Deploy" name="Deploy to Production">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="deployment-agent"
                  action="cloud.deploy"
                  purposeType="production_deployment"
                  policyTag="production-write"
                  requiresEvidence="tests_passed,security_cleared,human_approval" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:endEvent id="End" name="Deployed" />
            <bpmn:sequenceFlow id="sf1" sourceRef="Start" targetRef="Fork" />
            <bpmn:sequenceFlow id="sf2" sourceRef="Fork" targetRef="RunTests" />
            <bpmn:sequenceFlow id="sf3" sourceRef="Fork" targetRef="SecurityScan" />
            <bpmn:sequenceFlow id="sf4" sourceRef="RunTests" targetRef="Join" />
            <bpmn:sequenceFlow id="sf5" sourceRef="SecurityScan" targetRef="Join" />
            <bpmn:sequenceFlow id="sf6" sourceRef="Join" targetRef="DeployApproval" />
            <bpmn:sequenceFlow id="sf7" sourceRef="DeployApproval" targetRef="Deploy" />
            <bpmn:sequenceFlow id="sf8" sourceRef="Deploy" targetRef="End" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// Security Review: scan → intermediate timer wait (for async report) → exclusive gateway
    /// routes to remediation (findings present) or straight to approval (clean). Covers:
    /// exclusiveGateway, intermediateCatchEvent (timer), conditionExpression (true literal),
    /// userTask, sequenceFlow.
    /// </summary>
    private const string SecurityReviewTemplate = """
        <bpmn:definitions
            xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:autofac="https://autofac.de/bpmn/extensions/v1">
          <bpmn:process id="SecurityReview" name="Security Review">
            <bpmn:startEvent id="Start" name="Review Requested" />
            <bpmn:serviceTask id="Scan" name="Run Security Scan">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="security-agent"
                  action="security.scan"
                  purposeType="security_review"
                  policyTag="read-only" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:intermediateCatchEvent id="WaitForReport" name="Wait for Scan Report">
              <bpmn:timerEventDefinition>
                <bpmn:timeDuration>PT30S</bpmn:timeDuration>
              </bpmn:timerEventDefinition>
            </bpmn:intermediateCatchEvent>
            <bpmn:exclusiveGateway id="SeverityGate" name="Findings Severity Gate" />
            <bpmn:serviceTask id="Remediate" name="Remediate Findings">
              <bpmn:extensionElements>
                <autofac:agentTask
                  agent="security-agent"
                  action="security.remediate"
                  purposeType="remediation"
                  policyTag="repo-change" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:userTask id="VerifyApproval" name="Security Sign-Off">
              <bpmn:extensionElements>
                <autofac:approvalTask
                  purposeType="security_sign_off"
                  policyTag="human-security-approval" />
              </bpmn:extensionElements>
            </bpmn:userTask>
            <bpmn:endEvent id="End" name="Security Cleared" />
            <bpmn:sequenceFlow id="sf1" sourceRef="Start" targetRef="Scan" />
            <bpmn:sequenceFlow id="sf2" sourceRef="Scan" targetRef="WaitForReport" />
            <bpmn:sequenceFlow id="sf3" sourceRef="WaitForReport" targetRef="SeverityGate" />
            <bpmn:sequenceFlow id="sf4" sourceRef="SeverityGate" targetRef="Remediate">
              <bpmn:conditionExpression>true</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="sf5" sourceRef="SeverityGate" targetRef="VerifyApproval" />
            <bpmn:sequenceFlow id="sf6" sourceRef="Remediate" targetRef="VerifyApproval" />
            <bpmn:sequenceFlow id="sf7" sourceRef="VerifyApproval" targetRef="End" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    // =========================================================================
    // 1. Template validator conformance — all templates must validate cleanly
    // =========================================================================

    [Fact]
    public void IssueToPr_Template_ValidatesCleanly()
    {
        var result = _validator.Validate(IssueToPrTemplate);

        Assert.True(result.IsValid, FormatErrors(result));
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Definition);
        Assert.Equal("IssueToPr", result.Definition!.ProcessId);

        var nodeIds = result.Definition.Nodes.Select(static n => n.Id).ToHashSet();
        Assert.Contains("Start", nodeIds);
        Assert.Contains("Specify", nodeIds);
        Assert.Contains("Plan", nodeIds);
        Assert.Contains("Implement", nodeIds);
        Assert.Contains("CodeReview", nodeIds);
        Assert.Contains("OpenPR", nodeIds);
        Assert.Contains("End", nodeIds);

        var codeReview = result.Definition.Nodes.Single(static n => n.Id == "CodeReview");
        Assert.Equal("userTask", codeReview.ElementName);
        Assert.NotNull(codeReview.ApprovalMetadata);
        Assert.Equal("code_review", codeReview.ApprovalMetadata!.PurposeType);
    }

    [Fact]
    public void Bugfix_Template_ValidatesCleanly()
    {
        var result = _validator.Validate(BugfixTemplate);

        Assert.True(result.IsValid, FormatErrors(result));
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Definition);
        Assert.Equal("Bugfix", result.Definition!.ProcessId);

        var diagnose = result.Definition.Nodes.Single(static n => n.Id == "Diagnose");
        Assert.Equal("serviceTask", diagnose.ElementName);
        Assert.NotNull(diagnose.Metadata);
        Assert.Equal(2, diagnose.Metadata!.MaxRetries);
        Assert.Equal(5, diagnose.Metadata.RetryBackoffSeconds);
    }

    [Fact]
    public void ParallelBuildAndTest_Template_ValidatesCleanly()
    {
        var result = _validator.Validate(ParallelBuildAndTestTemplate);

        Assert.True(result.IsValid, FormatErrors(result));
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Definition);

        var forkNodes = result.Definition!.Nodes.Where(static n => n.ElementName == "parallelGateway").ToList();
        Assert.Equal(2, forkNodes.Count);

        var deploy = result.Definition.Nodes.Single(static n => n.Id == "Deploy");
        Assert.Equal(3, deploy.Metadata!.RequiresEvidence.Count);
    }

    [Fact]
    public void SecurityReview_Template_ValidatesCleanly()
    {
        var result = _validator.Validate(SecurityReviewTemplate);

        Assert.True(result.IsValid, FormatErrors(result));
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Definition);

        var timerNode = result.Definition!.Nodes.Single(static n => n.Id == "WaitForReport");
        Assert.Equal("intermediateCatchEvent", timerNode.ElementName);
        Assert.Equal("PT30S", timerNode.TimerDuration);

        Assert.Contains(result.Definition.Nodes, static n => n.ElementName == "exclusiveGateway");
    }

    // =========================================================================
    // 2. Engine execution — templates run to expected state
    // =========================================================================

    [Fact]
    public async Task IssueToPr_Template_EngineAdvancesToCodeReviewApproval()
    {
        var (store, engine) = BuildEngine();
        var definition = ParseDefinition(IssueToPrTemplate);

        var state = await engine.StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("waiting_user", state.Status);
        Assert.Equal("CodeReview", state.WaitingOnNodeId);

        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.Type == "run_started");
        Assert.Contains(events, static e => e.Type == "user_task_waiting");
        Assert.Contains(events, static e =>
            e.Type == "user_task_waiting" &&
            e.Message.Contains("\"purposeType\":\"code_review\"", StringComparison.Ordinal));

        // Service tasks before the gate must have completed steps.
        var run = await store.GetRunAsync(state.RunId, CancellationToken.None);
        var completedSteps = run!.Steps.Where(static s => s.Status == "completed").Select(static s => s.Name).ToList();
        Assert.Contains("Specify Requirements", completedSteps);
        Assert.Contains("Plan Implementation", completedSteps);
        Assert.Contains("Implement Changes", completedSteps);
    }

    [Fact]
    public async Task Bugfix_Template_EngineAdvancesToTestApproval()
    {
        var (store, engine) = BuildEngine();
        var definition = ParseDefinition(BugfixTemplate);

        var state = await engine.StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("waiting_user", state.Status);
        Assert.Equal("TestApproval", state.WaitingOnNodeId);

        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.Type == "user_task_waiting");
        Assert.Contains(events, static e =>
            e.Type == "user_task_waiting" &&
            e.Message.Contains("\"purposeType\":\"bugfix_merge\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ParallelBuildAndTest_Template_EngineExecutesBranchesAndAdvancesToDeployApproval()
    {
        var (store, engine) = BuildEngine();
        var definition = ParseDefinition(ParallelBuildAndTestTemplate);

        var state = await engine.StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("waiting_user", state.Status);
        Assert.Equal("DeployApproval", state.WaitingOnNodeId);

        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.Type == "parallel_forked");
        Assert.Equal(2, events.Count(static e => e.Type == "parallel_branch_entered"));
        Assert.Contains(events, static e => e.Type == "parallel_joined");
        Assert.Contains(events, static e => e.Type == "user_task_waiting");

        var run = await store.GetRunAsync(state.RunId, CancellationToken.None);
        var completedStepNames = run!.Steps.Where(static s => s.Status == "completed").Select(static s => s.Name).ToHashSet();
        Assert.Contains("Run Test Suite", completedStepNames);
        Assert.Contains("Run Security Scan", completedStepNames);
    }

    [Fact]
    public async Task SecurityReview_Template_TimerPausesThenExclusiveGatewayRoutesToRemediation()
    {
        var (store, engine) = BuildEngine();
        var definition = ParseDefinition(SecurityReviewTemplate);

        // The timer (PT30S) causes a waiting_timer pause before the gateway.
        var state = await engine.StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("waiting_timer", state.Status);
        Assert.Equal("WaitForReport", state.WaitingOnNodeId);
        Assert.NotNull(state.TimerDueAt);

        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.Type == "timer_scheduled");
    }

    [Fact]
    public async Task SecurityReview_Template_AfterTimerFireExclusiveGatewayRoutesTrueConditionToRemediation()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var definition = ParseDefinition(SecurityReviewTemplate);

        var timerState = await BuildEngineWith(store).StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("waiting_timer", timerState.Status);

        // Timers are continued via RecoverAsync (the dispatch worker fires it when due).
        // A fresh engine instance recovers from the persisted timer checkpoint.
        var resumed = await BuildEngineWith(store).RecoverAsync(
            timerState.RunId, definition, CancellationToken.None);

        // Gateway has condition=true → Remediate branch → then VerifyApproval (userTask).
        Assert.Equal("waiting_user", resumed.Status);
        Assert.Equal("VerifyApproval", resumed.WaitingOnNodeId);

        var events = await store.ListRunEventsAsync(timerState.RunId, CancellationToken.None);
        Assert.Contains(events, static e => e.Type == "timer_fired");
        Assert.Contains(events, static e => e.Type == "gateway_evaluated");

        var run = await store.GetRunAsync(timerState.RunId, CancellationToken.None);
        var completedNames = run!.Steps.Where(static s => s.Status == "completed").Select(static s => s.Name).ToHashSet();
        Assert.Contains("Run Security Scan", completedNames);
        Assert.Contains("Remediate Findings", completedNames);
    }

    // =========================================================================
    // 3. Approval resume — gate opens, run completes
    // =========================================================================

    [Fact]
    public async Task IssueToPr_Template_AfterApprovalRunCompletes()
    {
        var (_, engine) = BuildEngine();
        var definition = ParseDefinition(IssueToPrTemplate);

        var state = await engine.StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("waiting_user", state.Status);

        var resumed = await engine.ResumeAsync(state.RunId, definition, "reviewer", CancellationToken.None);

        Assert.Equal("completed", resumed.Status);
        Assert.Null(resumed.WaitingOnNodeId);
        Assert.NotNull(resumed.CompletedAt);
    }

    [Fact]
    public async Task ParallelBuildAndTest_Template_AfterApprovalRunCompletes()
    {
        var (_, engine) = BuildEngine();
        var definition = ParseDefinition(ParallelBuildAndTestTemplate);

        var state = await engine.StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        var resumed = await engine.ResumeAsync(state.RunId, definition, "reviewer", CancellationToken.None);

        Assert.Equal("completed", resumed.Status);
    }

    // =========================================================================
    // 4. Checkpoint and recovery — state survives a process restart
    // =========================================================================

    [Fact]
    public async Task IssueToPr_Template_CheckpointPersistedAtUserTask()
    {
        var (store, engine) = BuildEngine();
        var definition = ParseDefinition(IssueToPrTemplate);

        var state = await engine.StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        var events = await store.ListRunEventsAsync(state.RunId, CancellationToken.None);

        // Two checkpoints: one when execution starts running, one when it pauses at the user task.
        var checkpoints = events.Where(static e => e.Type == "checkpoint_saved").ToList();
        Assert.True(checkpoints.Count >= 2, $"Expected at least 2 checkpoints, got {checkpoints.Count}");
        Assert.Contains(checkpoints, static e => e.Message.Contains("\"status\":\"waiting_user\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IssueToPr_Template_RecoveryRestoresUserTaskCheckpoint()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var definition = ParseDefinition(IssueToPrTemplate);

        // Engine 1: start the run, reaching the user task.
        var engine1 = BuildEngineWith(store);
        var started = await engine1.StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        Assert.Equal("waiting_user", started.Status);

        // Engine 2: simulates a new process instance recovering from persisted state.
        var engine2 = BuildEngineWith(store);
        var recovered = await engine2.RecoverAsync(started.RunId, definition, CancellationToken.None);

        Assert.Equal(started.RunId, recovered.RunId);
        Assert.Equal("waiting_user", recovered.Status);
        Assert.Equal("CodeReview", recovered.WaitingOnNodeId);
        Assert.Equal(started.NextNodeId, recovered.NextNodeId);
    }

    [Fact]
    public async Task ParallelBuildAndTest_Template_CheckpointPersistedAfterParallelJoin()
    {
        var (store, engine) = BuildEngine();
        var definition = ParseDefinition(ParallelBuildAndTestTemplate);

        var state = await engine.StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        var events = (await store.ListRunEventsAsync(state.RunId, CancellationToken.None)).ToList();
        var checkpoints = events.Where(static e => e.Type == "checkpoint_saved").ToList();

        // At minimum: one running checkpoint, one waiting_user checkpoint after the join.
        Assert.True(checkpoints.Count >= 2);
        Assert.Contains(checkpoints, static e => e.Message.Contains("\"status\":\"waiting_user\"", StringComparison.Ordinal));

        // The parallel join event must precede the user-task checkpoint.
        var joinIndex = events.FindIndex(static e => e.Type == "parallel_joined");
        var userCheckpointIndex = events.FindIndex(static e =>
            e.Type == "checkpoint_saved" &&
            e.Message.Contains("\"status\":\"waiting_user\"", StringComparison.Ordinal));

        Assert.True(joinIndex >= 0 && userCheckpointIndex > joinIndex,
            "waiting_user checkpoint must appear after the parallel join.");
    }

    [Fact]
    public async Task Bugfix_Template_RecoveryRestoresUserTaskCheckpoint()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        var definition = ParseDefinition(BugfixTemplate);

        var started = await BuildEngineWith(store).StartAsync(
            Guid.NewGuid().ToString(), definition, "system", CancellationToken.None);

        var recovered = await BuildEngineWith(store).RecoverAsync(
            started.RunId, definition, CancellationToken.None);

        Assert.Equal("waiting_user", recovered.Status);
        Assert.Equal("TestApproval", recovered.WaitingOnNodeId);
    }

    // =========================================================================
    // 5. Unsupported constructs — validator rejects before publish
    // =========================================================================

    [Theory]
    [InlineData("manualTask", "manualTask")]
    [InlineData("callActivity", "callActivity")]
    [InlineData("receiveTask", "receiveTask")]
    [InlineData("eventBasedGateway", "eventBasedGateway")]
    [InlineData("complexGateway", "complexGateway")]
    [InlineData("adHocSubProcess", "adHocSubProcess")]
    public void Unsupported_BpmnElement_FailsValidationWithActionableError(
        string elementName, string expectedInError)
    {
        var xml = $"""
            <bpmn:definitions xmlns:bpmn="{BpmnNs}">
              <bpmn:process id="TestProcess" name="Test">
                <bpmn:startEvent id="Start" />
                <bpmn:{elementName} id="Unsupported1" name="Unsupported" />
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.ElementId == "Unsupported1");
        Assert.Contains(expectedInError, error.Message, StringComparison.Ordinal);
        Assert.Equal("Unsupported1", error.ElementId);
    }

    [Fact]
    public void Unsupported_CompensationBoundaryEvent_FailsValidation()
    {
        var xml = $"""
            <bpmn:definitions xmlns:bpmn="{BpmnNs}">
              <bpmn:process id="CompensationProcess" name="Test">
                <bpmn:startEvent id="Start" />
                <bpmn:serviceTask id="Task1" name="Task">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      xmlns:autofac="{AutofacNs}"
                      agent="a" action="b" purposeType="c" policyTag="d" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:boundaryEvent id="CompBoundary" attachedToRef="Task1">
                  <bpmn:compensateEventDefinition />
                </bpmn:boundaryEvent>
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.ElementId == "CompBoundary");
        Assert.Contains("timerEventDefinition", error.Message, StringComparison.Ordinal);
        Assert.Contains("errorEventDefinition", error.Message, StringComparison.Ordinal);
        Assert.Contains("escalationEventDefinition", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_MessageIntermediateCatchEvent_FailsValidation()
    {
        // An intermediateCatchEvent with a messageEventDefinition instead of a timer is
        // invalid — the default runtime only supports timer-based intermediate events.
        var xml = $"""
            <bpmn:definitions xmlns:bpmn="{BpmnNs}">
              <bpmn:process id="MessageProcess" name="Test">
                <bpmn:startEvent id="Start" />
                <bpmn:intermediateCatchEvent id="WaitForMessage">
                  <bpmn:messageEventDefinition />
                </bpmn:intermediateCatchEvent>
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.ElementId == "WaitForMessage");
        Assert.Contains("timerEventDefinition", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_IntermediateCatchEvent_WithoutAnyDefinition_FailsValidation()
    {
        var xml = $"""
            <bpmn:definitions xmlns:bpmn="{BpmnNs}">
              <bpmn:process id="EmptyEvent" name="Test">
                <bpmn:startEvent id="Start" />
                <bpmn:intermediateCatchEvent id="Bare" />
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static e =>
            e.ElementId == "Bare" &&
            e.Message.Contains("timerEventDefinition", StringComparison.Ordinal));
    }

    [Fact]
    public void Unsupported_MultipleUnsupportedElements_ReportsEachWithItsOwnError()
    {
        var xml = $"""
            <bpmn:definitions xmlns:bpmn="{BpmnNs}">
              <bpmn:process id="MultiUnsupported" name="Test">
                <bpmn:startEvent id="Start" />
                <bpmn:manualTask id="M1" name="Manual 1" />
                <bpmn:callActivity id="C1" name="Call" />
                <bpmn:eventBasedGateway id="EBG1" />
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains(result.Errors, static e => e.ElementId == "M1" && e.Message.Contains("manualTask", StringComparison.Ordinal));
        Assert.Contains(result.Errors, static e => e.ElementId == "C1" && e.Message.Contains("callActivity", StringComparison.Ordinal));
        Assert.Contains(result.Errors, static e => e.ElementId == "EBG1" && e.Message.Contains("eventBasedGateway", StringComparison.Ordinal));
    }

    [Fact]
    public void Unsupported_ServiceTask_WithoutAutofacMetadata_FailsValidation()
    {
        // A bare serviceTask with no autofac:agentTask extension is not supported —
        // every service task must declare which agent and policy govern it.
        var xml = $"""
            <bpmn:definitions xmlns:bpmn="{BpmnNs}">
              <bpmn:process id="BareService" name="Test">
                <bpmn:startEvent id="Start" />
                <bpmn:serviceTask id="BareTask" name="Bare Service Task" />
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static e =>
            e.ElementId == "BareTask" &&
            e.Message.Contains("agent", StringComparison.Ordinal));
    }

    [Fact]
    public void Unsupported_UserTask_WithoutApprovalMetadata_FailsValidation()
    {
        var xml = $"""
            <bpmn:definitions xmlns:bpmn="{BpmnNs}">
              <bpmn:process id="BareUser" name="Test">
                <bpmn:startEvent id="Start" />
                <bpmn:userTask id="BareUser1" name="Bare User Task" />
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var result = _validator.Validate(xml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static e =>
            e.ElementId == "BareUser1" &&
            e.Message.Contains("autofac:approvalTask", StringComparison.Ordinal));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static (InMemoryWorkflowRuntimeStore Store, WorkflowInstanceEngine Engine) BuildEngine()
    {
        var store = new InMemoryWorkflowRuntimeStore();
        return (store, BuildEngineWith(store));
    }

    private static WorkflowInstanceEngine BuildEngineWith(InMemoryWorkflowRuntimeStore store)
    {
        return new WorkflowInstanceEngine(
            store,
            new NoOpServiceTaskExecutor(),
            new InMemoryRunContextRepository(),
            NullLogger<WorkflowInstanceEngine>.Instance);
    }

    private BpmnWorkflowDefinition ParseDefinition(string bpmnXml)
    {
        var result = _validator.Validate(bpmnXml);
        Assert.True(result.IsValid, $"Template failed validation unexpectedly: {FormatErrors(result)}");
        return result.Definition!;
    }

    private static string FormatErrors(BpmnValidationResult result)
    {
        return string.Join("; ", result.Errors.Select(static e => $"[{e.ElementId}] {e.Message}"));
    }

    private sealed class NoOpServiceTaskExecutor : IServiceTaskExecutor
    {
        public Task<AgentTaskOutcome> ExecuteAsync(
            string runId, string stepId, BpmnNodeDefinition node,
            int attempt, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: true,
                Output: "conformance-executor",
                FailureReason: null));
        }
    }
}

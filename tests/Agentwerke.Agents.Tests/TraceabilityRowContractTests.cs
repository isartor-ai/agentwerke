using System.Text;
using System.Text.RegularExpressions;
using Agentwerke.Agents.Tools;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;
using Agentwerke.Storage.Artifacts;
using Agentwerke.Workflows.Bpmn;
using Agentwerke.Workflows.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

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

    /// <summary>
    /// Runs the real <c>examples/v-model-pilot-thread.bpmn</c> artifact through
    /// <see cref="WorkflowInstanceEngine"/> from <c>Start</c> to <c>WaitForVerificationResult</c>,
    /// resumes with the CI callback payload shape emitted by <c>examples/ci/agentwerke-verify.yml</c>,
    /// executes <c>IngestTestResults</c> via <see cref="VerificationIngestTestResultsTool"/> using the
    /// BPMN node's <c>tool.input.*</c> templates rendered against the engine's run context, and proves
    /// <see cref="TraceabilityRowBuilder.Build"/> produces the full requirement-to-test traceability rows (#210, #211).
    /// </summary>
    [Fact]
    public async Task VModelPilotThread_ExecutesFromRequirementThroughWebhookResumeToTraceabilityRows()
    {
        var bpmnPath = FindRepositoryFile("examples", "v-model-pilot-thread.bpmn");
        var validation = new BpmnWorkflowValidator().Validate(await File.ReadAllTextAsync(bpmnPath));
        Assert.True(validation.IsValid);
        var definition = validation.Definition!;

        var store = new InMemoryWorkflowRuntimeStore();
        var runContextRepo = new InMemoryRunContextRepository();
        var executor = new PilotThreadServiceTaskExecutor(runContextRepo);
        var engine = new WorkflowInstanceEngine(
            store,
            executor,
            runContextRepo,
            NullLogger<WorkflowInstanceEngine>.Instance);

        var run = await store.CreateRunAsync("VModelPilotThread", "system", CancellationToken.None);
        var waitingState = await engine.StartAsync(
            new WorkflowEngineStartRequest("VModelPilotThread", definition, "system", ExistingRunId: run.Id),
            CancellationToken.None);

        Assert.Equal("waiting_external", waitingState.Status);
        Assert.Equal("WaitForVerificationResult", waitingState.WaitingOnNodeId);
        Assert.Equal(run.Id, waitingState.WaitingExternalCorrelationKey);

        const string reportXml = """
            <testsuites>
              <testsuite name="Agentwerke.Workflows.Tests.dll" tests="2" failures="0" time="0.31">
                <testcase classname="Agentwerke.Workflows.Tests.PilotTests" name="VerifiesRequirement42" time="0.015" />
                <testcase classname="Agentwerke.Workflows.Tests.PilotTests" name="PreservesAuditTrail" time="0.012" />
              </testsuite>
            </testsuites>
            """;

        var completedState = await engine.ResumeAsync(
            new WorkflowEngineResumeRequest(
                RunId: run.Id,
                Definition: definition,
                ApprovedBy: null,
                ExternalCorrelationKey: run.Id,
                ExternalPayload: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["requirement_id"] = "42",
                    ["ci_run_url"] = "https://github.com/octo/app/actions/runs/98765",
                    ["report_xml"] = reportXml
                },
                ResumedBy: "github-actions"),
            CancellationToken.None);

        Assert.Equal("completed", completedState.Status);

        var completedRun = await store.GetRunAsync(run.Id, CancellationToken.None);
        Assert.NotNull(completedRun);

        var rows = TraceabilityRowBuilder.Build(completedRun!);
        Assert.Equal(2, rows.Count);

        Assert.All(rows, row =>
        {
            Assert.Equal("github", row.RequirementProvider);
            Assert.Equal("42", row.RequirementId);
            Assert.Equal("https://github.com/octo/app/issues/42", row.RequirementUrl);
            Assert.Equal("98765", row.CiRunId);
            Assert.Equal("https://github.com/octo/app/actions/runs/98765", row.CiRunUrl);
            Assert.Equal("passed", row.Status);
            Assert.EndsWith("-junit.xml", row.EvidenceArtifact, StringComparison.Ordinal);
        });

        Assert.Contains(rows, r => r.TestId == "Agentwerke.Workflows.Tests.PilotTests.VerifiesRequirement42");
        Assert.Contains(rows, r => r.TestId == "Agentwerke.Workflows.Tests.PilotTests.PreservesAuditTrail");
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

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Agentwerke.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (Agentwerke.sln) from the test output directory.");
        }

        return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
    }

    private sealed class PilotThreadServiceTaskExecutor(IRunContextRepository runContextRepository) : IServiceTaskExecutor
    {
        private static readonly Regex TemplatePattern = new("{{\\s*([a-zA-Z0-9_.-]+)\\s*}}", RegexOptions.Compiled);

        public async Task<AgentTaskOutcome> ExecuteAsync(
            string runId,
            string stepId,
            BpmnNodeDefinition node,
            int attempt,
            CancellationToken cancellationToken,
            AgentExecutionProgressReporter? progressReporter = null)
        {
            if (string.Equals(node.Id, "ReadRequirement", StringComparison.Ordinal))
            {
                return new AgentTaskOutcome(
                    Succeeded: true,
                    Output: "Read issue #42",
                    FailureReason: null,
                    ExternalActions:
                    [
                        new ExternalActionRecord(
                            Provider: "github",
                            Action: "read_issue",
                            Status: "completed",
                            ResourceId: "42",
                            ResourceUrl: "https://github.com/octo/app/issues/42",
                            Summary: "GitHub issue #42")
                    ]);
            }

            if (string.Equals(node.Id, "IngestTestResults", StringComparison.Ordinal))
            {
                var entries = await runContextRepository.GetAllAsync(runId, cancellationToken);
                var runContext = entries.ToDictionary(static e => e.Key, static e => e.Value, StringComparer.OrdinalIgnoreCase);
                var metadata = node.Metadata!.RuntimeContract!.Metadata;
                var toolInput = metadata
                    .Where(static pair => pair.Key.StartsWith("tool.input.", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(
                        static pair => pair.Key["tool.input.".Length..],
                        pair => TemplatePattern.Replace(
                            pair.Value,
                            match => runContext.TryGetValue(match.Groups[1].Value, out var val) ? val : match.Value),
                        StringComparer.OrdinalIgnoreCase);

                var toolResult = await new VerificationIngestTestResultsTool(new NoOpArtifactStorage()).ExecuteAsync(
                    new AgentToolExecutionContext(
                        RunId: runId,
                        StepId: stepId,
                        AgentName: node.Metadata.Agent,
                        Action: node.Metadata.Action,
                        Environment: node.Metadata.Environment,
                        PurposeType: node.Metadata.PurposeType,
                        PolicyTag: node.Metadata.PolicyTag,
                        Attempt: attempt),
                    toolInput,
                    cancellationToken);

                return new AgentTaskOutcome(
                    Succeeded: toolResult.Succeeded,
                    Output: toolResult.Output,
                    FailureReason: toolResult.FailureReason,
                    Artifacts: toolResult.Artifacts,
                    ExternalActions: toolResult.ExternalActions);
            }

            return new AgentTaskOutcome(
                Succeeded: true,
                Output: $"completed {node.Id}",
                FailureReason: null);
        }
    }

    private sealed class InMemoryWorkflowRuntimeStore : IWorkflowRuntimeStore
    {
        private readonly Dictionary<string, WorkflowRun> _runs = [];

        public Task<WorkflowRun> CreateRunAsync(
            string workflowDefinitionId, string? initiator, CancellationToken cancellationToken,
            string? correlationId = null)
        {
            var run = new WorkflowRun
            {
                Id = Guid.NewGuid().ToString(),
                WorkflowId = workflowDefinitionId,
                Status = "created",
                RequestedBy = initiator ?? "unknown",
                StartedAt = DateTime.UtcNow.ToString("o")
            };
            _runs[run.Id] = run;
            return Task.FromResult(run);
        }

        public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult(_runs.GetValueOrDefault(runId));

        public Task<IReadOnlyList<WorkflowEvent>> ListRunEventsAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkflowEvent>>(
                _runs.TryGetValue(runId, out var run) ? run.Events.AsReadOnly() : []);

        public Task AppendEventAsync(string runId, string type, string message, CancellationToken cancellationToken)
        {
            if (_runs.TryGetValue(runId, out var run))
            {
                run.Events.Add(new WorkflowEvent
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = type,
                    Message = message,
                    CreatedAt = DateTime.UtcNow.ToString("o")
                });
            }

            return Task.CompletedTask;
        }

        public Task UpdateRunStatusAsync(string runId, string status, string? completedAt, CancellationToken cancellationToken)
        {
            if (_runs.TryGetValue(runId, out var run))
            {
                run.Status = status;
                run.CompletedAt = completedAt;
            }

            return Task.CompletedTask;
        }

        public Task<WorkflowRunStep> CreateStepAsync(
            string runId, string nodeId, string? nodeName, string nodeType,
            string? agentName, CancellationToken cancellationToken)
        {
            var step = new WorkflowRunStep
            {
                Id = $"step_{Guid.NewGuid():N}",
                Name = nodeName ?? nodeId,
                Type = nodeType,
                Status = "running",
                StartedAt = DateTime.UtcNow.ToString("o"),
                AgentName = agentName
            };
            if (_runs.TryGetValue(runId, out var run))
            {
                run.Steps.Add(step);
            }

            return Task.FromResult(step);
        }

        public Task UpdateStepStatusAsync(
            string stepId, string status, string? output, string? error, string? completedAt,
            PolicyDecision? policyDecision,
            AgentRuntimeSnapshot? runtimeSnapshot,
            CancellationToken cancellationToken)
        {
            var step = _runs.Values
                .SelectMany(static r => r.Steps)
                .FirstOrDefault(s => string.Equals(s.Id, stepId, StringComparison.Ordinal));
            if (step is not null)
            {
                step.Status = status;
                step.Output = output;
                step.Error = error;
                step.CompletedAt = completedAt;
                step.PolicyDecision = policyDecision;
                step.RuntimeSnapshot = runtimeSnapshot;
            }

            return Task.CompletedTask;
        }
    }
}

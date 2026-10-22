# V-model Process Evaluation Test

This evaluation checks whether Agentwerke can represent a V-model software
delivery process as BPMN while preserving `agentwerke:*` metadata for agents,
approvals, and external test-result waits.

## Artifact

- BPMN: `examples/v-model-process.bpmn`
- Process id: `VModelProcessEvaluation`
- Validation test:
  `tests/Agentwerke.Workflows.Tests/EvaluationBpmnArtifactsTests.cs`

## Scenario Coverage

| V-model concern | BPMN element |
| --- | --- |
| Requirements analysis | `DraftRequirements` agent task |
| Requirements baseline approval | `ApproveRequirementsBaseline` user task |
| System architecture/design | `DraftSystemArchitecture` agent task |
| Component design | `DraftComponentDesign` agent task |
| Implementation | `ImplementComponents` sandboxed agent task |
| Unit verification | `GenerateUnitTestPlan`, `WaitUnitTestResults`, and `UnitTestOutcomeGateway` |
| Component traceability gate | `ValidateComponentTraceability` |
| Integration verification | `GenerateIntegrationTestPlan`, `WaitIntegrationTestResults`, and `IntegrationTestOutcomeGateway` |
| Architecture traceability gate | `ValidateArchitectureTraceability` |
| System verification | `AnalyzeSystemTestResults`, `WaitSystemTestResults`, and `SystemTestOutcomeGateway` |
| Failure analysis & escalation | `SummarizeVerificationFailures` and `EndVerificationFailed` (only on bounded failure escalation) |
| Acceptance verification | `PrepareAcceptanceTest` |
| Acceptance sign-off | `ApproveAcceptanceSignoff` user task |
| Traceability evidence | `PrepareTraceabilityReport` |

## Evaluation Questions

### Can Agentwerke BPMN express the V-model shape clearly?

Yes for a curated evaluation workflow. The diagram uses a left-to-right V layout
with decomposition phases descending toward implementation and verification
phases rising toward acceptance. The traceability gates are explicit agent
tasks, so users can inspect how requirements and design artifacts connect to
verification evidence.

### Can `agentwerke:*` metadata capture the needed behavior?

Yes for this test shape:

- `agentwerke:agentTask` captures the responsible agent, action, execution mode,
  sandbox profile, permission level, prompts, evidence requirements, and
  traceability metadata.
- `agentwerke:approvalTask` captures both human gates.
- `agentwerke:externalEvent` captures unit, integration, and system test-result
  callbacks.

### Can standard BPMN tooling open the diagram?

The artifact is plain BPMN 2.0 XML with Agentwerke extensions under
`bpmn:extensionElements`, plus BPMNDI shape and edge metadata. Standard BPMN
editors should preserve unknown extension elements even if they do not provide a
custom UI for editing them.

### Can validation catch malformed Agentwerke metadata?

The regression test removes `agentwerke:agentTask` metadata from one task and
asserts that `BpmnWorkflowValidator` returns an actionable validation error.

### Does the run/evaluation log have enough traceability?

The BPMN artifact provides stable phase names, evidence keys, and external
event correlation keys. A dry run should capture each phase below.

## Dry-run Checklist

Use these inputs:

- `change_id`: `VMODEL-001`
- `build_id`: `build-vmodel-001`
- `repository`: a sandbox repository with a small service and tests

Expected run timeline:

1. `DraftRequirements` records a requirements baseline with requirement IDs.
2. `ApproveRequirementsBaseline` blocks until a human approves the baseline.
3. `DraftSystemArchitecture` maps architecture decisions to requirements.
4. `DraftComponentDesign` maps component obligations to architecture.
5. `ImplementComponents` runs in a repo-write sandbox and records changed files.
6. `GenerateUnitTestPlan` maps unit tests to component obligations.
7. `WaitUnitTestResults` resumes on `test.unit.completed` with
   `{{input.build_id}}:unit`.
8. `UnitTestOutcomeGateway` evaluates the unit verification result:
   - on pass, advances to `ValidateComponentTraceability`;
   - on `{{event.conclusion}} == "failure"`, loops back to `ImplementComponents` for rework;
   - on `{{event.rework_exhausted}} == "true"`, routes to `SummarizeVerificationFailures` and terminates at `EndVerificationFailed`.
9. `ValidateComponentTraceability` checks component-design-to-unit-test
   coverage.
10. `GenerateIntegrationTestPlan` maps integration tests to component interfaces.
11. `WaitIntegrationTestResults` resumes on `test.integration.completed` with
    `{{input.build_id}}:integration`.
12. `IntegrationTestOutcomeGateway` evaluates the integration verification result:
    - on pass, advances to `ValidateArchitectureTraceability`;
    - on `{{event.conclusion}} == "failure"`, loops back to `DraftSystemArchitecture` for architecture/component rework;
    - on `{{event.rework_exhausted}} == "true"`, routes to `SummarizeVerificationFailures` and terminates at `EndVerificationFailed`.
13. `ValidateArchitectureTraceability` checks architecture-to-integration-test
    coverage.
14. `AnalyzeSystemTestResults` prepares the system verification scope.
15. `WaitSystemTestResults` resumes on `test.system.completed` with
    `{{input.build_id}}:system`.
16. `SystemTestOutcomeGateway` evaluates the system verification result:
    - on pass, advances directly to `PrepareAcceptanceTest` (skipping `SummarizeVerificationFailures`);
    - on `{{event.conclusion}} == "failure"`, loops back to `DraftRequirements` for requirements/baseline rework;
    - on `{{event.rework_exhausted}} == "true"`, routes to `SummarizeVerificationFailures` and terminates at `EndVerificationFailed`.
17. `PrepareAcceptanceTest` assembles acceptance evidence.
18. `ApproveAcceptanceSignoff` blocks until human acceptance.
19. `PrepareTraceabilityReport` emits the final traceability matrix.

Failure-path dry-run checks:

- Resuming `WaitUnitTestResults` with `conclusion: "failure"` re-enters `ImplementComponents` -> `GenerateUnitTestPlan` -> `WaitUnitTestResults` instead of advancing to `ValidateComponentTraceability` or `End`.
- Resuming `WaitIntegrationTestResults` with `conclusion: "failure"` re-enters `DraftSystemArchitecture` -> `DraftComponentDesign` -> `ImplementComponents` instead of advancing to `ValidateArchitectureTraceability` or `End`.
- Resuming `WaitSystemTestResults` with `conclusion: "failure"` re-enters `DraftRequirements` -> `ApproveRequirementsBaseline` instead of advancing to `PrepareAcceptanceTest` or `End`.
- Resuming any verification wait with `rework_exhausted: "true"` executes `SummarizeVerificationFailures` and stops at `EndVerificationFailed` rather than looping indefinitely or reaching `End`.

## Gaps and UX Friction

- Standard BPMN editors can preserve the `agentwerke:*` extension XML, but they
  do not provide a friendly form for editing Agentwerke metadata.
- The run timeline can show each phase and event, but users still need a
  traceability matrix view to compare requirements, design artifacts, tests, and
  approvals side by side.
- External test-result waits rely on correctly configured message names and
  correlation keys; the editor should make those values easier to inspect.
- `agentwerke:metadata` key/value pairs are expressive enough for this
  evaluation, but a table editor would reduce XML-level editing.

## Recommended Product Changes

- Add a first-class traceability matrix view in run detail.
- Add a V-model template card after this evaluation is accepted.
- Let the desktop BPMN editor display `agentwerke:metadata` key/value pairs in a
  compact table.
- Add a manual test that round-trips this artifact through the desktop editor
  and Camunda Modeler.

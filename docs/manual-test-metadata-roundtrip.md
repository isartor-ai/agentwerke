# Manual Test Scenario: `agentwerke:metadata` Round-Trip

Version: Draft v0.1
Status: Active
Date: 2026-07-14

## Purpose

This scenario is the manual half of recommended product change #4 in
`docs/evaluations/v-model-process-test.md`: round-trip the V-model artifact
(`examples/v-model-process.bpmn`) through both the Agentwerke desktop editor and
the Camunda Modeler, confirming that `agentwerke:*` extension elements —
especially `<agentwerke:metadata>` key/value pairs and the CDATA
`<agentwerke:prompt>` bodies — survive an import→export with no data loss.

The **automated** half is covered in CI and does not need a human:

- `web/src/test/bpmnMetadataRoundTrip.unit.test.ts` round-trips through the
  Agentwerke moddle (`bpmn-moddle` + `agentwerkeModdleDescriptor`) and asserts
  metadata and prompt elements are preserved and that editor-created rows
  serialize back to `<agentwerke:metadata>`.
- `tests/Agentwerke.Workflows.Tests/EvaluationBpmnArtifactsTests.cs` asserts the
  artifact validates and that its metadata parses into the runtime contract.

Run those first; this document only covers what CI cannot exercise: the two
desktop editors.

## Background

`web/src/bpmn/agentwerkeModdle.ts` models the `Metadata` (key/value) and `Prompt`
(body) child types on `AgentTask`. Without those types, bpmn-js silently drops
the child elements on the first import→export. If this manual test shows metadata
or prompts disappearing, the moddle descriptor is the first place to look.

Camunda Modeler does **not** know the `agentwerke` namespace and provides no form
for editing it, but per the BPMN 2.0 spec it must preserve unknown
`bpmn:extensionElements` content verbatim. This test verifies that expectation.

## Prerequisites

- The Agentwerke web app running locally (`web` dev server + API), signed in with
  an Operator or Admin identity.
- Camunda Modeler (desktop) installed — https://camunda.com/download/modeler/.
  Use the Camunda 7 or "Platform" diagram mode; the namespace handling is the
  same for the round-trip.
- A local copy of `examples/v-model-process.bpmn`.

## Part A — Agentwerke desktop editor round-trip

1. Open the workflow designer, choose **Import BPMN**, and select
   `examples/v-model-process.bpmn`.
2. Confirm the canvas renders all 19 nodes (it keeps the artifact's own layout;
   no auto-layout is applied because the file already has BPMNDI).
3. Select the **Requirements Analysis** task (`DraftRequirements`). In the
   properties panel, expand **Agentwerke — Metadata**. Confirm two rows:
   - `phase` = `requirements`
   - `traceability.produces` = `requirements_baseline`
4. Add a row: click **+**, set key `owner`, value `qa-lead`.
5. Edit the existing `phase` row's value to `requirements_v2`.
6. Click **Export BPMN** and save the file.
7. Reopen the exported file with **Import BPMN**. Re-select `DraftRequirements`
   and confirm:
   - the `owner` = `qa-lead` row persisted,
   - `phase` = `requirements_v2` persisted,
   - `traceability.produces` is unchanged,
   - the task's `<agentwerke:prompt>` still drives the same agent behavior
     (spot-check by opening the exported XML: the CDATA prompt block is intact).

Expected: every metadata edit and the prompt survive the export→import cycle.

## Part B — Camunda Modeler round-trip

1. In Camunda Modeler, open `examples/v-model-process.bpmn` (the original, not the
   Part A export).
2. Confirm the diagram opens and renders the V shape. Camunda shows no Agentwerke
   properties UI — that is expected.
3. Make a **BPMN-native** edit that forces a re-serialize, e.g. drag the
   `ImplementComponents` task to a new position, or rename the `End` event.
4. Save (Ctrl/Cmd-S), overwriting or writing a new file.
5. Open the saved file in a text editor (or re-import it into the Agentwerke
   designer) and verify the `agentwerke:*` content survived:
   - each `<agentwerke:agentTask>` still has its attributes,
   - `<agentwerke:metadata key="…" value="…"/>` rows are present on the tasks that
     had them,
   - `<agentwerke:prompt><![CDATA[…]]></agentwerke:prompt>` bodies are intact,
   - `<agentwerke:approvalTask>` and `<agentwerke:externalEvent>` are present.
6. Import the Camunda-saved file into the Agentwerke designer and confirm the
   **Agentwerke — Metadata** table still lists the expected rows.

Expected: Camunda preserves all `agentwerke:*` extension elements verbatim even
though it cannot edit them; the file remains a valid Agentwerke workflow.

## Failure triage

| Symptom | Likely cause |
| --- | --- |
| Metadata rows or prompts vanish after the Agentwerke export | A child element is not modeled in `web/src/bpmn/agentwerkeModdle.ts`; add the missing type. The automated round-trip test should have caught this. |
| Camunda drops `agentwerke:*` content | The extension elements are not well-formed, or Camunda was set to strip unknown extensions — re-check the source XML is valid and namespaced. |
| Agentwerke re-import shows no Metadata group | The task is not a service/script task, or `extensionElements` lost its `agentwerke:agentTask` — inspect the XML. |
| Designer validation fails after round-trip | Run the file through `BpmnWorkflowValidator` (backend) and read the actionable error; a dropped required attribute is the usual cause. |

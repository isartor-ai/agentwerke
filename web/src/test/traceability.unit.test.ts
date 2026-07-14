import { parseTraceability, collectEvidenceKeys } from '../bpmn/traceability';
import { AGENTWERKE_NS_PREFIX, AGENTWERKE_NS_URI } from '../bpmn/constants';

const BPMN = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
    xmlns:${AGENTWERKE_NS_PREFIX}="${AGENTWERKE_NS_URI}"
    id="defs">
  <bpmn:process id="P" isExecutable="true">
    <bpmn:startEvent id="Start" name="Start" />
    <bpmn:serviceTask id="DraftRequirements" name="Requirements Analysis">
      <bpmn:extensionElements>
        <agentwerke:agentTask agent="analyst" action="draft">
          <agentwerke:metadata key="phase" value="requirements" />
          <agentwerke:metadata key="traceability.produces" value="requirements_baseline" />
        </agentwerke:agentTask>
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:userTask id="ApproveRequirements" name="Approve Requirements">
      <bpmn:extensionElements>
        <agentwerke:approvalTask purposeType="requirements_baseline" policyTag="req" />
      </bpmn:extensionElements>
    </bpmn:userTask>
    <bpmn:serviceTask id="UnitGate" name="Component Traceability Gate">
      <bpmn:extensionElements>
        <agentwerke:agentTask agent="tester" action="gate"
          requiresEvidence="component_design_baseline,unit_test_results">
          <agentwerke:metadata key="phase" value="component_traceability" />
          <agentwerke:metadata key="traceability.verifies" value="component_design_baseline" />
          <agentwerke:metadata key="traceability.connects" value="component_design_baseline:unit_test_results" />
        </agentwerke:agentTask>
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:intermediateCatchEvent id="WaitUnit" name="Wait Unit Test Results">
      <bpmn:extensionElements>
        <agentwerke:externalEvent messageName="test.unit.completed" correlationKeyTemplate="{{input.build_id}}:unit" />
      </bpmn:extensionElements>
      <bpmn:messageEventDefinition />
    </bpmn:intermediateCatchEvent>
  </bpmn:process>
</bpmn:definitions>`;

describe('parseTraceability', () => {
  it('extracts governed nodes in flow order with their extension type', () => {
    const nodes = parseTraceability(BPMN);

    expect(nodes.map((n) => n.id)).toEqual([
      'DraftRequirements',
      'ApproveRequirements',
      'UnitGate',
      'WaitUnit',
    ]);
    expect(nodes.map((n) => n.extension)).toEqual([
      'agentTask',
      'approvalTask',
      'agentTask',
      'externalEvent',
    ]);
    // The plain startEvent has no Agentwerke extension and is excluded.
    expect(nodes.find((n) => n.id === 'Start')).toBeUndefined();
  });

  it('reads phase, produces, verifies, connects, and requiresEvidence', () => {
    const nodes = parseTraceability(BPMN);
    const requirements = nodes.find((n) => n.id === 'DraftRequirements')!;
    const gate = nodes.find((n) => n.id === 'UnitGate')!;

    expect(requirements.phase).toBe('requirements');
    expect(requirements.produces).toEqual(['requirements_baseline']);

    expect(gate.verifies).toEqual(['component_design_baseline']);
    expect(gate.connects).toEqual(['component_design_baseline:unit_test_results']);
    expect(gate.requiresEvidence).toEqual(['component_design_baseline', 'unit_test_results']);
  });

  it('collects the distinct evidence keys across produce/require', () => {
    expect(collectEvidenceKeys(parseTraceability(BPMN))).toEqual([
      'component_design_baseline',
      'requirements_baseline',
      'unit_test_results',
    ]);
  });

  it('returns an empty array for blank or metadata-free workflows', () => {
    expect(parseTraceability('')).toEqual([]);
    expect(
      parseTraceability(
        '<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"><bpmn:process id="P"><bpmn:serviceTask id="T" name="T" /></bpmn:process></bpmn:definitions>',
      ),
    ).toEqual([]);
  });
});

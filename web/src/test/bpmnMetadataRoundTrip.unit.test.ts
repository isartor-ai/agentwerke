import BpmnModdle from 'bpmn-moddle';
import { agentwerkeModdleDescriptor } from '../bpmn/agentwerkeModdle';
import { AGENTWERKE_NS_PREFIX, AGENTWERKE_NS_URI } from '../bpmn/constants';

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Phase 2 / recommended change #3–#4 (docs/evaluations/v-model-process-test.md):
 * the moddle must model <agentwerke:metadata> (and the CDATA <agentwerke:prompt>) so bpmn-js
 * reads them into business objects and re-serializes them losslessly. Without modeled types,
 * these children are dropped on the first import→export, silently corrupting authored workflows.
 */
const AGENT_TASK_BPMN = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
    xmlns:${AGENTWERKE_NS_PREFIX}="${AGENTWERKE_NS_URI}"
    id="defs" targetNamespace="${AGENTWERKE_NS_URI}">
  <bpmn:process id="P" isExecutable="true">
    <bpmn:serviceTask id="DraftRequirements" name="Requirements Analysis">
      <bpmn:extensionElements>
        <agentwerke:agentTask agent="analyst" action="vmodel.requirements.draft" permissionLevel="read-only">
          <agentwerke:metadata key="phase" value="requirements" />
          <agentwerke:metadata key="traceability.produces" value="requirements_baseline" />
          <agentwerke:prompt><![CDATA[Draft the requirements baseline for {{input.change_id}}.]]></agentwerke:prompt>
        </agentwerke:agentTask>
      </bpmn:extensionElements>
    </bpmn:serviceTask>
  </bpmn:process>
</bpmn:definitions>`;

function newModdle() {
  return new BpmnModdle({ [AGENTWERKE_NS_PREFIX]: agentwerkeModdleDescriptor });
}

function findAgentTask(definitions: any) {
  const process = definitions.rootElements.find((el: any) => el.$type === 'bpmn:Process');
  const task = process.flowElements.find((el: any) => el.id === 'DraftRequirements');
  const ext = task.extensionElements?.values ?? [];
  return ext.find((el: any) => el.$type === `${AGENTWERKE_NS_PREFIX}:AgentTask`);
}

describe('agentwerke:metadata moddle modeling', () => {
  it('parses <agentwerke:metadata> children into typed business objects', async () => {
    const { rootElement } = await newModdle().fromXML(AGENT_TASK_BPMN);
    const agentTask = findAgentTask(rootElement);

    expect(agentTask).toBeDefined();
    expect(agentTask.metadata).toHaveLength(2);
    expect(agentTask.metadata.map((m: { key: string }) => m.key)).toEqual([
      'phase',
      'traceability.produces',
    ]);
    expect(agentTask.metadata[0].value).toBe('requirements');
  });

  it('round-trips metadata and prompt without dropping them', async () => {
    const moddle = newModdle();
    const { rootElement } = await moddle.fromXML(AGENT_TASK_BPMN);
    const { xml } = await moddle.toXML(rootElement);

    expect(xml).toContain('key="phase"');
    expect(xml).toContain('value="requirements"');
    expect(xml).toContain('key="traceability.produces"');
    expect(xml).toContain('value="requirements_baseline"');
    // The agent prompt must survive too — losing it would silently break the workflow.
    expect(xml).toContain('Draft the requirements baseline for {{input.change_id}}.');
  });

  it('serializes edited metadata back to <agentwerke:metadata> elements', async () => {
    const moddle = newModdle();
    const { rootElement } = await moddle.fromXML(AGENT_TASK_BPMN);
    const agentTask = findAgentTask(rootElement);

    // Simulate the properties-panel adding a row.
    agentTask.metadata.push(
      moddle.create(`${AGENTWERKE_NS_PREFIX}:Metadata`, { key: 'owner', value: 'qa-lead' }),
    );
    const { xml } = await moddle.toXML(rootElement);

    expect(xml).toContain('key="owner"');
    expect(xml).toContain('value="qa-lead"');
  });
});

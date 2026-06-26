import { createEmptyDiagram } from '../bpmn/constants';
import { ensureDiagramInterchange } from '../bpmn/layout';

const LAYOUT_LESS_BPMN = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_Layoutless">
  <bpmn:process id="LayoutlessFlow" isExecutable="true">
    <bpmn:startEvent id="Start" />
    <bpmn:sequenceFlow id="Flow_Start_End" sourceRef="Start" targetRef="End" />
    <bpmn:endEvent id="End" />
  </bpmn:process>
</bpmn:definitions>`;

const LAYOUT_LESS_AUTOFAC_BPMN = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:autofac="https://autofac.ai/bpmn" id="Definitions_Autofac">
  <bpmn:process id="AutofacFlow" isExecutable="true">
    <bpmn:serviceTask id="Implement" name="Implement">
      <bpmn:extensionElements>
        <autofac:agentTask agent="developer" action="repo.write" />
      </bpmn:extensionElements>
    </bpmn:serviceTask>
  </bpmn:process>
</bpmn:definitions>`;

describe('BPMN layout normalization', () => {
  it('adds BPMNDI to layout-less BPMN before bpmn-js import', async () => {
    const xml = await ensureDiagramInterchange(LAYOUT_LESS_BPMN);

    expect(xml).toContain('<bpmndi:BPMNDiagram');
    expect(xml).toContain('bpmnElement="LayoutlessFlow"');
    expect(xml).toContain('bpmnElement="Start"');
    expect(xml).toContain('bpmnElement="End"');
  });

  it('keeps diagrams with existing BPMNDI unchanged', async () => {
    const xmlWithLayout = createEmptyDiagram('AlreadyLaidOut');

    await expect(ensureDiagramInterchange(xmlWithLayout)).resolves.toBe(xmlWithLayout);
  });

  it('preserves Autofac extension metadata while adding BPMNDI', async () => {
    const xml = await ensureDiagramInterchange(LAYOUT_LESS_AUTOFAC_BPMN);

    expect(xml).toContain('<bpmndi:BPMNDiagram');
    expect(xml).toContain('autofac:agentTask');
    expect(xml).toContain('agent="developer"');
    expect(xml).toContain('action="repo.write"');
  });

  it('surfaces parser errors for invalid BPMN XML', async () => {
    await expect(ensureDiagramInterchange('<bpmn:definitions>')).rejects.toThrow(/BPMN XML is invalid/i);
  });
});

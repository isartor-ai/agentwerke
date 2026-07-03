import { act, render, screen, waitFor } from '@testing-library/react';
import { createRef } from 'react';
import { createEmptyDiagram } from '../bpmn/constants';
import { BpmnModeler, type BpmnModelerHandle } from '../components/BpmnModeler';
import { BpmnViewer } from '../components/BpmnViewer';

const bpmnMocks = vi.hoisted(() => ({
  modelerInstances: [] as Array<{
    imports: string[];
    clear: ReturnType<typeof vi.fn>;
    destroy: ReturnType<typeof vi.fn>;
    importXML: ReturnType<typeof vi.fn>;
  }>,
  viewerInstances: [] as Array<{
    imports: string[];
    destroy: ReturnType<typeof vi.fn>;
    importXML: ReturnType<typeof vi.fn>;
  }>,
}));

vi.mock('bpmn-js/lib/Modeler', () => ({
  default: class FakeModeler {
    imports: string[] = [];
    clear = vi.fn();
    destroy = vi.fn();
    importXML = vi.fn(async (xml: string) => {
      this.imports.push(xml);
      if (xml.includes('SlowBrokenFlow')) {
        await new Promise((resolve) => setTimeout(resolve, 10));
        throw new Error('modeler import failed');
      }
      if (xml.includes('BrokenFlow')) {
        throw new Error('modeler import failed');
      }
      return { warnings: [] };
    });

    constructor() {
      bpmnMocks.modelerInstances.push(this);
    }

    saveXML = vi.fn(async () => ({ xml: this.imports[this.imports.length - 1] ?? '' }));
    on = vi.fn();
    off = vi.fn();
    get = vi.fn((name: string) => {
      if (name === 'canvas') {
        return { zoom: vi.fn() };
      }
      if (name === 'overlays') {
        return { clear: vi.fn(), add: vi.fn() };
      }
      if (name === 'elementRegistry') {
        return { forEach: vi.fn(), get: vi.fn() };
      }
      return {};
    });
  },
}));

vi.mock('bpmn-js-properties-panel', () => ({
  BpmnPropertiesPanelModule: {},
  BpmnPropertiesProviderModule: {},
}));

vi.mock('../bpmn/agentwerkeModule', () => ({
  agentwerkeModules: [],
}));

vi.mock('../bpmn/agentwerkeModdle', () => ({
  agentwerkeModdleDescriptor: {},
}));

vi.mock('bpmn-js/lib/NavigatedViewer', () => ({
  default: class FakeNavigatedViewer {
    imports: string[] = [];
    destroy = vi.fn();
    importXML = vi.fn(async (xml: string) => {
      this.imports.push(xml);
      if (xml.includes('BrokenFlow')) {
        throw new Error('viewer import failed');
      }
      return { warnings: [] };
    });

    constructor() {
      bpmnMocks.viewerInstances.push(this);
    }

    get = vi.fn((name: string) => {
      if (name === 'canvas') {
        return { zoom: vi.fn(), addMarker: vi.fn(), removeMarker: vi.fn() };
      }
      if (name === 'elementRegistry') {
        return { forEach: vi.fn() };
      }
      if (name === 'eventBus') {
        return { on: vi.fn() };
      }
      return {};
    });
  },
}));

const LAYOUT_LESS_BPMN = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_Layoutless">
  <bpmn:process id="LayoutlessFlow" isExecutable="true">
    <bpmn:startEvent id="Start" />
    <bpmn:sequenceFlow id="Flow_Start_End" sourceRef="Start" targetRef="End" />
    <bpmn:endEvent id="End" />
  </bpmn:process>
</bpmn:definitions>`;

describe('BPMN import components', () => {
  beforeEach(() => {
    bpmnMocks.modelerInstances.length = 0;
    bpmnMocks.viewerInstances.length = 0;
  });

  it('auto-layouts layout-less BPMN before importing it into the read-only viewer', async () => {
    render(<BpmnViewer xml={LAYOUT_LESS_BPMN} />);

    await waitFor(() => {
      expect(bpmnMocks.viewerInstances[0]?.importXML).toHaveBeenCalled();
    });

    expect(bpmnMocks.viewerInstances[0].imports[0]).toContain('<bpmndi:BPMNDiagram');
    expect(bpmnMocks.viewerInstances[0].imports[0]).toContain('bpmnElement="LayoutlessFlow"');
  });

  it('surfaces read-only viewer import failures', async () => {
    render(<BpmnViewer xml={createEmptyDiagram('BrokenFlow')} />);

    expect(await screen.findByRole('alert')).toHaveTextContent('viewer import failed');
  });

  it('auto-layouts layout-less BPMN before importing it into the editor', async () => {
    const ref = createRef<BpmnModelerHandle>();
    render(<BpmnModeler ref={ref} />);

    await waitFor(() => {
      expect(ref.current).not.toBeNull();
    });

    await act(async () => {
      await ref.current?.importXML(LAYOUT_LESS_BPMN);
    });

    expect(bpmnMocks.modelerInstances[0].imports[0]).toContain('<bpmndi:BPMNDiagram');
    expect(bpmnMocks.modelerInstances[0].imports[0]).toContain('bpmnElement="LayoutlessFlow"');
  });

  it('recovers from failed editor imports and keeps the latest overlapping import', async () => {
    const ref = createRef<BpmnModelerHandle>();
    const onError = vi.fn();
    const onImportSuccess = vi.fn();
    render(<BpmnModeler ref={ref} onError={onError} onImportSuccess={onImportSuccess} />);

    await waitFor(() => {
      expect(ref.current).not.toBeNull();
    });

    await act(async () => {
      await ref.current?.importXML(createEmptyDiagram('BrokenFlow'));
    });
    expect(onError).toHaveBeenCalledWith('modeler import failed');

    onError.mockClear();
    onImportSuccess.mockClear();

    await act(async () => {
      const staleFailure = ref.current?.importXML(createEmptyDiagram('SlowBrokenFlow'));
      const finalImport = ref.current?.importXML(createEmptyDiagram('FinalValidFlow'));
      await Promise.all([staleFailure, finalImport]);
    });

    const imports = bpmnMocks.modelerInstances[0].imports;
    expect(imports[imports.length - 1]).toContain('FinalValidFlow');
    expect(onError).not.toHaveBeenCalled();
    expect(onImportSuccess).toHaveBeenCalledTimes(1);
  });
});

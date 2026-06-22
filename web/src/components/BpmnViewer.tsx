import { useEffect, useRef } from 'react';
import NavigatedViewer from 'bpmn-js/lib/NavigatedViewer';
import type { RunStatus } from '../types';

import 'bpmn-js/dist/assets/diagram-js.css';
import 'bpmn-js/dist/assets/bpmn-js.css';
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn.css';

export interface BpmnViewerProps {
  /** BPMN 2.0 XML to display */
  xml: string;
  /** Map of BPMN element name → RunStatus; drives canvas accent markers */
  stepStatuses?: Record<string, RunStatus>;
  /** Name of the currently selected step's BPMN element, if any */
  selectedStepName?: string | null;
  /** Called with a step's BPMN element name when it's clicked (null when re-clicking the selected one) */
  onSelectStep?: (name: string | null) => void;
  className?: string;
}

// CSS marker class per RunStatus applied via canvas.addMarker(elementId, class)
const STATUS_MARKER: Partial<Record<RunStatus, string>> = {
  completed: 'raf-completed',
  running: 'raf-running',
  awaiting_approval: 'raf-awaiting',
  failed: 'raf-failed',
  blocked: 'raf-failed',
  cancelled: 'raf-cancelled',
};

const SELECTED_MARKER = 'raf-selected';
const ALL_MARKERS = [...new Set(Object.values(STATUS_MARKER))].filter(Boolean) as string[];

function applyMarkers(
  viewer: NavigatedViewer,
  statuses: Record<string, RunStatus>,
  selectedStepName: string | null | undefined,
) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const canvas = viewer.get<any>('canvas');
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const elementRegistry = viewer.get<any>('elementRegistry');
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  elementRegistry.forEach((element: any) => {
    const name: string | undefined = element.businessObject?.name;
    for (const m of ALL_MARKERS) canvas.removeMarker(element.id, m);
    canvas.removeMarker(element.id, SELECTED_MARKER);
    if (name && statuses[name]) {
      const marker = STATUS_MARKER[statuses[name]];
      if (marker) canvas.addMarker(element.id, marker);
    }
    if (name && name === selectedStepName) canvas.addMarker(element.id, SELECTED_MARKER);
  });
}

export function BpmnViewer({ xml, stepStatuses, selectedStepName, onSelectStep, className }: BpmnViewerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewerRef = useRef<NavigatedViewer | null>(null);
  const onSelectStepRef = useRef(onSelectStep);
  onSelectStepRef.current = onSelectStep;
  const selectedStepNameRef = useRef(selectedStepName);
  selectedStepNameRef.current = selectedStepName;
  const statusesRef = useRef(stepStatuses);
  statusesRef.current = stepStatuses;

  // Owns the full lifecycle of one viewer instance: create, wire click handling,
  // import, destroy. Keeping create+import in a single effect (rather than
  // splitting "create" and "import" across two effects sharing a ref) matters
  // under React StrictMode, which double-invokes effects in dev: with two
  // effects, the second mount's import could silently skip (via a stale
  // "already loaded this xml" ref) while running against a *different* viewer
  // instance than the one that actually imported it, or race against the first
  // instance's in-flight import resolving after it was already destroyed —
  // both hit bpmn-js/diagram-js internals with "Cannot read properties of
  // undefined (reading 'root-0')". Scoping everything to one effect keyed on
  // `xml` means each viewer instance's import is fully owned by the effect run
  // that created it, with `cancelled` from that same closure guarding the
  // async resolution.
  useEffect(() => {
    if (!containerRef.current || !xml) return;

    const viewer = new NavigatedViewer({ container: containerRef.current });
    viewerRef.current = viewer;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    viewer.get<any>('eventBus').on('element.click', (event: any) => {
      const name: string | undefined = event.element?.businessObject?.name;
      if (!name) return;
      onSelectStepRef.current?.(name === selectedStepNameRef.current ? null : name);
    });

    let cancelled = false;

    void viewer.importXML(xml)
      .then(() => {
        if (cancelled) return;
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        viewer.get<any>('canvas').zoom('fit-viewport');
        applyMarkers(viewer, statusesRef.current ?? {}, selectedStepNameRef.current);
      })
      .catch(() => {});

    return () => {
      cancelled = true;
      viewer.destroy();
      viewerRef.current = null;
    };
  }, [xml]);

  // Re-apply markers in place when status/selection changes — no reimport needed.
  useEffect(() => {
    const viewer = viewerRef.current;
    if (!viewer) return;
    applyMarkers(viewer, stepStatuses ?? {}, selectedStepName);
  }, [stepStatuses, selectedStepName]);

  return (
    <div
      ref={containerRef}
      className={`bpmn-viewer${className ? ` ${className}` : ''}`}
    />
  );
}

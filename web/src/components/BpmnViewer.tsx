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

const ALL_MARKERS = [...new Set(Object.values(STATUS_MARKER))].filter(Boolean) as string[];

function applyMarkers(viewer: NavigatedViewer, statuses: Record<string, RunStatus>) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const canvas = viewer.get<any>('canvas');
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const elementRegistry = viewer.get<any>('elementRegistry');
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  elementRegistry.forEach((element: any) => {
    const name: string | undefined = element.businessObject?.name;
    for (const m of ALL_MARKERS) canvas.removeMarker(element.id, m);
    if (name && statuses[name]) {
      const marker = STATUS_MARKER[statuses[name]];
      if (marker) canvas.addMarker(element.id, marker);
    }
  });
}

export function BpmnViewer({ xml, stepStatuses, className }: BpmnViewerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewerRef = useRef<NavigatedViewer | null>(null);
  const loadedXmlRef = useRef('');

  useEffect(() => {
    if (!containerRef.current) return;
    const viewer = new NavigatedViewer({ container: containerRef.current });
    viewerRef.current = viewer;
    return () => {
      viewer.destroy();
      viewerRef.current = null;
    };
  }, []);

  useEffect(() => {
    const viewer = viewerRef.current;
    if (!viewer || !xml) return;

    const statuses = stepStatuses ?? {};

    // Skip reimport when only stepStatuses changed
    if (xml === loadedXmlRef.current) {
      applyMarkers(viewer, statuses);
      return;
    }

    loadedXmlRef.current = xml;
    let cancelled = false;

    void viewer.importXML(xml)
      .then(() => {
        if (cancelled) return;
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        viewer.get<any>('canvas').zoom('fit-viewport');
        applyMarkers(viewer, statuses);
      })
      .catch(() => {});

    return () => { cancelled = true; };
    // loadedXmlRef is a stable ref — not a dep
  }, [xml, stepStatuses]);

  return (
    <div
      ref={containerRef}
      className={`bpmn-viewer${className ? ` ${className}` : ''}`}
    />
  );
}

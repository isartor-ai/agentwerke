import { useEffect, useRef, useState } from 'react';
import NavigatedViewer from 'bpmn-js/lib/NavigatedViewer';
import { ensureDiagramInterchange } from '../bpmn/layout';
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
  waiting_external: 'raf-awaiting',
  needs_config: 'raf-awaiting',
  failed: 'raf-failed',
  blocked: 'raf-failed',
  cancelled: 'raf-cancelled',
};

const SELECTED_MARKER = 'raf-selected';
const ALL_MARKERS = [...new Set(Object.values(STATUS_MARKER))].filter(Boolean) as string[];
const BPMN_VIEW_PADDING = 56;
const BPMN_MAX_AUTO_ZOOM = 1.35;

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

function focusWorkflow(viewer: NavigatedViewer) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const canvas = viewer.get<any>('canvas');
  const viewbox = canvas.viewbox?.();
  const inner = viewbox?.inner;
  const outer = viewbox?.outer;

  if (!inner || !outer || inner.width <= 0 || inner.height <= 0 || outer.width <= 0 || outer.height <= 0) {
    canvas.zoom('fit-viewport');
    return;
  }

  const widthWithPadding = inner.width + BPMN_VIEW_PADDING * 2;
  const heightWithPadding = inner.height + BPMN_VIEW_PADDING * 2;
  const scale = Math.min(
    BPMN_MAX_AUTO_ZOOM,
    outer.width / widthWithPadding,
    outer.height / heightWithPadding,
  );

  if (!Number.isFinite(scale) || scale <= 0) {
    canvas.zoom('fit-viewport');
    return;
  }

  const viewboxWidth = outer.width / scale;
  const viewboxHeight = outer.height / scale;

  canvas.viewbox({
    x: inner.x - (viewboxWidth - inner.width) / 2,
    y: inner.y - (viewboxHeight - inner.height) / 2,
    width: viewboxWidth,
    height: viewboxHeight,
  });
}

export function BpmnViewer({ xml, stepStatuses, selectedStepName, onSelectStep, className }: BpmnViewerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewerRef = useRef<NavigatedViewer | null>(null);
  const [importError, setImportError] = useState<string | null>(null);
  const hasImportedRef = useRef(false);
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
    hasImportedRef.current = false;

    let animationFrame: number | null = null;
    const fitCurrentDiagram = () => {
      if (animationFrame !== null) {
        cancelAnimationFrame(animationFrame);
      }

      animationFrame = requestAnimationFrame(() => {
        if (cancelled || !hasImportedRef.current) {
          return;
        }
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const canvas = viewer.get<any>('canvas');
        canvas.resized?.();
        focusWorkflow(viewer);
        applyMarkers(viewer, statusesRef.current ?? {}, selectedStepNameRef.current);
      });
    };

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    viewer.get<any>('eventBus').on('element.click', (event: any) => {
      const name: string | undefined = event.element?.businessObject?.name;
      if (!name) return;
      onSelectStepRef.current?.(name === selectedStepNameRef.current ? null : name);
    });

    let cancelled = false;

    setImportError(null);

    const resizeObserver = typeof ResizeObserver === 'undefined' || !containerRef.current
      ? null
      : new ResizeObserver(() => fitCurrentDiagram());

    resizeObserver?.observe(containerRef.current);

    void ensureDiagramInterchange(xml)
      .then((xmlWithLayout) => viewer.importXML(xmlWithLayout))
      .then(() => {
        if (cancelled) return;
        hasImportedRef.current = true;
        fitCurrentDiagram();
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setImportError(error instanceof Error ? error.message : 'Failed to render BPMN diagram.');
      });

    return () => {
      cancelled = true;
      hasImportedRef.current = false;
      resizeObserver?.disconnect();
      if (animationFrame !== null) {
        cancelAnimationFrame(animationFrame);
      }
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
    <>
      <div
        ref={containerRef}
        className={`bpmn-viewer${className ? ` ${className}` : ''}`}
      />
      {importError ? (
        <p className="validation-error" role="alert">
          {importError}
        </p>
      ) : null}
    </>
  );
}

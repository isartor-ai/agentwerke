import type { BpmnViewerProps } from '../BpmnViewer';

/**
 * Test double for BpmnViewer. NavigatedViewer needs SVG layout unavailable in jsdom.
 * Renders step statuses as data attributes so tests can assert on them.
 */
export function BpmnViewer({ xml, stepStatuses, className }: BpmnViewerProps) {
  return (
    <div data-testid="bpmn-viewer-mock" className={className}>
      {xml ? <span data-testid="viewer-xml-loaded">xml-loaded</span> : null}
      {stepStatuses
        ? Object.entries(stepStatuses).map(([name, status]) => (
            <span key={name} data-step-name={name} data-step-status={status} />
          ))
        : null}
    </div>
  );
}

import { AGENTWERKE_NS_URI } from './constants';

/**
 * Client-side traceability extraction (rec #1 of docs/evaluations/v-model-process-test.md).
 * RunDetail already fetches the workflow BPMN for the diagram; parsing its `agentwerke:metadata`
 * traceability keys here lets the run's Traceability tab join authored structure (what each phase
 * produces/consumes/verifies and what evidence it requires) with runtime status — no backend work.
 */

const BPMN_NS = 'http://www.omg.org/spec/BPMN/20100524/MODEL';

/** BPMN flow nodes that can carry an Agentwerke extension worth tracing. */
const TRACEABLE_ELEMENTS = [
  'serviceTask',
  'scriptTask',
  'userTask',
  'receiveTask',
  'intermediateCatchEvent',
];

export type TraceabilityExtension = 'agentTask' | 'approvalTask' | 'externalEvent';

export interface TraceabilityNode {
  id: string;
  name: string;
  /** BPMN local name, e.g. serviceTask, userTask, intermediateCatchEvent. */
  elementType: string;
  /** Which Agentwerke extension governs the node, if any. */
  extension?: TraceabilityExtension;
  /** V-model phase from `agentwerke:metadata key="phase"`. */
  phase?: string;
  /** Evidence keys this node produces / consumes / verifies (traceability.* metadata). */
  produces: string[];
  consumes: string[];
  verifies: string[];
  /** `traceability.connects` links, kept as raw "a:b" strings. */
  connects: string[];
  /** Evidence gate keys from the `requiresEvidence` attribute. */
  requiresEvidence: string[];
}

function splitList(value: string | null | undefined): string[] {
  if (!value) return [];
  return value
    .split(',')
    .map((entry) => entry.trim())
    .filter(Boolean);
}

function firstChildByLocalName(parent: Element, localName: string): Element | null {
  for (const child of Array.from(parent.children)) {
    if (child.localName === localName && child.namespaceURI === AGENTWERKE_NS_URI) {
      return child;
    }
  }
  return null;
}

function findExtension(node: Element): { element: Element; type: TraceabilityExtension } | null {
  const extensionElements = Array.from(node.children).find(
    (child) => child.localName === 'extensionElements',
  );
  if (!extensionElements) return null;

  for (const type of ['agentTask', 'approvalTask', 'externalEvent'] as const) {
    const element = firstChildByLocalName(extensionElements, type);
    if (element) return { element, type };
  }
  return null;
}

function readMetadata(extension: Element): Map<string, string> {
  const entries = new Map<string, string>();
  for (const child of Array.from(extension.children)) {
    if (child.localName !== 'metadata' || child.namespaceURI !== AGENTWERKE_NS_URI) continue;
    const key = child.getAttribute('key')?.trim();
    if (!key) continue;
    const value = child.getAttribute('value') ?? child.textContent ?? '';
    entries.set(key, value.trim());
  }
  return entries;
}

/**
 * Parses the traceability-relevant nodes from a workflow's BPMN XML, in document (flow) order.
 * Only nodes carrying an Agentwerke extension are returned; an empty result means the workflow
 * has no traceability metadata to show.
 */
export function parseTraceability(bpmnXml: string): TraceabilityNode[] {
  if (!bpmnXml.trim()) return [];

  const doc = new DOMParser().parseFromString(bpmnXml, 'application/xml');
  if (doc.getElementsByTagName('parsererror').length > 0) return [];

  const seen = new Set<string>();
  const nodes: TraceabilityNode[] = [];

  for (const localName of TRACEABLE_ELEMENTS) {
    const elements = doc.getElementsByTagNameNS(BPMN_NS, localName);
    for (const element of Array.from(elements)) {
      const id = element.getAttribute('id');
      if (!id || seen.has(id)) continue;

      const found = findExtension(element);
      if (!found) continue;
      seen.add(id);

      const metadata = readMetadata(found.element);
      nodes.push({
        id,
        name: element.getAttribute('name') ?? id,
        elementType: localName,
        extension: found.type,
        phase: metadata.get('phase') || undefined,
        produces: splitList(metadata.get('traceability.produces')),
        consumes: splitList(metadata.get('traceability.consumes')),
        verifies: splitList(metadata.get('traceability.verifies')),
        connects: splitList(metadata.get('traceability.connects')),
        requiresEvidence: splitList(found.element.getAttribute('requiresEvidence')),
      });
    }
  }

  // Preserve document order across the different element-name queries above.
  return sortByDocumentOrder(doc, nodes);
}

function sortByDocumentOrder(doc: Document, nodes: TraceabilityNode[]): TraceabilityNode[] {
  const order = new Map<string, number>();
  let index = 0;
  const walk = (element: Element) => {
    const id = element.getAttribute?.('id');
    if (id && !order.has(id)) order.set(id, index++);
    for (const child of Array.from(element.children)) walk(child);
  };
  const root = doc.documentElement;
  if (root) walk(root);
  return [...nodes].sort((a, b) => (order.get(a.id) ?? 0) - (order.get(b.id) ?? 0));
}

/** All distinct evidence keys referenced across the nodes (produced or required). */
export function collectEvidenceKeys(nodes: TraceabilityNode[]): string[] {
  const keys = new Set<string>();
  for (const node of nodes) {
    for (const key of node.produces) keys.add(key);
    for (const key of node.requiresEvidence) keys.add(key);
  }
  return [...keys].sort();
}

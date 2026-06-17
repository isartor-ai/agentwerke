import type { TemplateDetail, TemplateFactoryConfiguration } from '../types';

const DEFAULT_AUTOFAC_NS = 'https://autofac.de/bpmn/extensions/v1';
const BPMN_NS = 'http://www.omg.org/spec/BPMN/20100524/MODEL';
const BPMNDI_NS = 'http://www.omg.org/spec/BPMN/20100524/DI';
const DC_NS = 'http://www.omg.org/spec/DD/20100524/DC';
const DI_NS = 'http://www.omg.org/spec/DD/20100524/DI';

const NODE_LAYOUT: Record<string, { width: number; height: number; y: number }> = {
  boundaryEvent: { width: 36, height: 36, y: 142 },
  endEvent: { width: 36, height: 36, y: 142 },
  exclusiveGateway: { width: 50, height: 50, y: 135 },
  intermediateCatchEvent: { width: 36, height: 36, y: 142 },
  parallelGateway: { width: 50, height: 50, y: 135 },
  scriptTask: { width: 100, height: 80, y: 120 },
  serviceTask: { width: 100, height: 80, y: 120 },
  startEvent: { width: 36, height: 36, y: 142 },
  subProcess: { width: 120, height: 90, y: 115 },
  userTask: { width: 100, height: 80, y: 120 },
};

interface Bounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

function selectedKeys(values: Record<string, boolean>): string[] {
  return Object.entries(values)
    .filter(([, selected]) => selected)
    .map(([key]) => key);
}

function getXmlParserError(document: Document): string | null {
  const parserError = document.querySelector('parsererror');
  return parserError?.textContent?.trim() || null;
}

function namespaceFor(document: Document, prefix: string, fallback: string): string {
  return document.documentElement.lookupNamespaceURI(prefix) ?? fallback;
}

function ensureNamespace(root: Element, prefix: string, namespaceUri: string) {
  if (!root.getAttribute(`xmlns:${prefix}`)) {
    root.setAttribute(`xmlns:${prefix}`, namespaceUri);
  }
}

function createNamespacedElement(document: Document, namespaceUri: string, qualifiedName: string) {
  return document.createElementNS(namespaceUri, qualifiedName);
}

function appendShape(document: Document, plane: Element, node: Element, bounds: Bounds) {
  const id = node.getAttribute('id');
  if (!id) return;

  const shape = createNamespacedElement(document, BPMNDI_NS, 'bpmndi:BPMNShape');
  shape.setAttribute('id', `${id}_di`);
  shape.setAttribute('bpmnElement', id);

  const shapeBounds = createNamespacedElement(document, DC_NS, 'dc:Bounds');
  shapeBounds.setAttribute('x', String(bounds.x));
  shapeBounds.setAttribute('y', String(bounds.y));
  shapeBounds.setAttribute('width', String(bounds.width));
  shapeBounds.setAttribute('height', String(bounds.height));

  shape.appendChild(shapeBounds);
  plane.appendChild(shape);
}

function appendWaypoint(document: Document, edge: Element, x: number, y: number) {
  const waypoint = createNamespacedElement(document, DI_NS, 'di:waypoint');
  waypoint.setAttribute('x', String(Math.round(x)));
  waypoint.setAttribute('y', String(Math.round(y)));
  edge.appendChild(waypoint);
}

function appendEdge(
  document: Document,
  plane: Element,
  sequenceFlow: Element,
  nodeBounds: Map<string, Bounds>,
) {
  const id = sequenceFlow.getAttribute('id');
  const sourceRef = sequenceFlow.getAttribute('sourceRef');
  const targetRef = sequenceFlow.getAttribute('targetRef');
  const source = sourceRef ? nodeBounds.get(sourceRef) : null;
  const target = targetRef ? nodeBounds.get(targetRef) : null;

  if (!id || !source || !target) return;

  const edge = createNamespacedElement(document, BPMNDI_NS, 'bpmndi:BPMNEdge');
  edge.setAttribute('id', `${id}_di`);
  edge.setAttribute('bpmnElement', id);

  appendWaypoint(document, edge, source.x + source.width, source.y + source.height / 2);
  appendWaypoint(document, edge, target.x, target.y + target.height / 2);

  plane.appendChild(edge);
}

function ensureDiagramInterchange(document: Document, process: Element | undefined) {
  if (!process || document.getElementsByTagNameNS(BPMNDI_NS, 'BPMNDiagram').length > 0) {
    return;
  }

  const root = document.documentElement;
  ensureNamespace(root, 'bpmndi', BPMNDI_NS);
  ensureNamespace(root, 'dc', DC_NS);
  ensureNamespace(root, 'di', DI_NS);
  if (!root.getAttribute('targetNamespace')) {
    root.setAttribute('targetNamespace', 'https://autofac.de/bpmn');
  }

  const diagram = createNamespacedElement(document, BPMNDI_NS, 'bpmndi:BPMNDiagram');
  diagram.setAttribute('id', 'BPMNDiagram_1');

  const plane = createNamespacedElement(document, BPMNDI_NS, 'bpmndi:BPMNPlane');
  plane.setAttribute('id', 'BPMNPlane_1');
  plane.setAttribute('bpmnElement', process.getAttribute('id') ?? 'Process_1');

  const processChildren = Array.from(process.children).filter(
    (child) => child.namespaceURI === BPMN_NS && child.getAttribute('id'),
  );
  const nodes = processChildren.filter((child) => child.localName !== 'sequenceFlow');
  const sequenceFlows = processChildren.filter((child) => child.localName === 'sequenceFlow');
  const nodeBounds = new Map<string, Bounds>();

  nodes.forEach((node, index) => {
    const id = node.getAttribute('id');
    if (!id) return;

    const layout = NODE_LAYOUT[node.localName] ?? NODE_LAYOUT.serviceTask;
    const bounds = {
      x: 152 + index * 170,
      y: layout.y,
      width: layout.width,
      height: layout.height,
    };
    nodeBounds.set(id, bounds);
    appendShape(document, plane, node, bounds);
  });

  sequenceFlows.forEach((flow) => appendEdge(document, plane, flow, nodeBounds));

  diagram.appendChild(plane);
  root.appendChild(diagram);
}

export function buildConfiguredTemplateBpmn(
  template: TemplateDetail,
  configuration: TemplateFactoryConfiguration,
): string {
  const parser = new DOMParser();
  const document = parser.parseFromString(template.bpmnXml, 'application/xml');
  const autofacNamespace = namespaceFor(document, 'autofac', DEFAULT_AUTOFAC_NS);
  const parserError = getXmlParserError(document);

  if (parserError) {
    throw new Error(`Template BPMN XML is invalid: ${parserError}`);
  }

  ensureNamespace(document.documentElement, 'autofac', autofacNamespace);

  const process = document.getElementsByTagNameNS(BPMN_NS, 'process')[0];
  if (process) {
    process.setAttribute('name', configuration.name.trim() || template.name);
    process.setAttributeNS(autofacNamespace, 'autofac:owner', configuration.owner.trim());
    process.setAttributeNS(autofacNamespace, 'autofac:description', configuration.description.trim());
    process.setAttributeNS(autofacNamespace, 'autofac:requiredInputs', template.requiredInputs.join(','));
    process.setAttributeNS(
      autofacNamespace,
      'autofac:inputDefaults',
      JSON.stringify(configuration.requiredInputs),
    );
    process.setAttributeNS(autofacNamespace, 'autofac:connectors', selectedKeys(configuration.connectors).join(','));
    process.setAttributeNS(autofacNamespace, 'autofac:policyLevel', configuration.policyLevel);
    process.setAttributeNS(autofacNamespace, 'autofac:evidence', selectedKeys(configuration.evidence).join(','));
  }

  for (const agentTask of Array.from(document.getElementsByTagNameNS(autofacNamespace, 'agentTask'))) {
    const currentAgent = agentTask.getAttribute('agent');
    const configuredAgent = currentAgent ? configuration.agentAssignments[currentAgent] : null;
    if (configuredAgent?.trim()) {
      agentTask.setAttribute('agent', configuredAgent.trim());
    }
  }

  const approvalTasks = Array.from(document.getElementsByTagNameNS(autofacNamespace, 'approvalTask'));
  approvalTasks.forEach((approvalTask, index) => {
    const role = template.approvalRoles[index] ?? template.approvalRoles[0];
    const assignee = role ? configuration.approvalAssignments[role] : null;
    if (assignee?.trim()) {
      approvalTask.setAttribute('approvalRole', assignee.trim());
    }
  });

  ensureDiagramInterchange(document, process);

  return new XMLSerializer().serializeToString(document);
}

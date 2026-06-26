import { layoutProcess } from 'bpmn-auto-layout';

const BPMNDI_NS = 'http://www.omg.org/spec/BPMN/20100524/DI';

function getXmlParserError(document: Document): string | null {
  const parserError = document.querySelector('parsererror');
  return parserError?.textContent?.trim() || null;
}

function hasDiagramInterchange(document: Document): boolean {
  return (
    document.getElementsByTagNameNS(BPMNDI_NS, 'BPMNDiagram').length > 0 ||
    document.getElementsByTagName('bpmndi:BPMNDiagram').length > 0
  );
}

/**
 * bpmn-js imports layout-less BPMN successfully but renders an empty canvas.
 * Add BPMNDI only when it is absent so authored diagrams keep their positions.
 */
export async function ensureDiagramInterchange(xml: string): Promise<string> {
  const trimmed = xml.trim();
  if (!trimmed) {
    return xml;
  }

  const document = new DOMParser().parseFromString(trimmed, 'application/xml');
  const parserError = getXmlParserError(document);
  if (parserError) {
    throw new Error(`BPMN XML is invalid: ${parserError}`);
  }

  if (hasDiagramInterchange(document)) {
    return xml;
  }

  return layoutProcess(xml);
}

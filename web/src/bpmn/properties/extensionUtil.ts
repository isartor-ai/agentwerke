import { getBusinessObject } from 'bpmn-js/lib/util/ModelUtil';

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Helpers for reading and writing Agentwerke extension elements
 * (`autofac:agentTask` / `autofac:approvalTask`) on a BPMN element's business
 * object. All writes go through `modeling.updateModdleProperties` so they are
 * undoable and trigger a re-render + change event.
 */

export interface ModdleServices {
  modeling: any;
  moddle: any;
}

/** Returns the typed extension element if present, else `undefined`. */
export function getExtension(element: any, type: string): any | undefined {
  const bo = getBusinessObject(element);
  const values = bo?.extensionElements?.values;
  if (!values) {
    return undefined;
  }
  return values.find((value: any) => value.$type === type);
}

/** Reads a single attribute off the typed extension element. */
export function getExtensionProperty(
  element: any,
  type: string,
  property: string,
): string {
  const extension = getExtension(element, type);
  const value = extension?.get ? extension.get(property) : extension?.[property];
  return value === undefined || value === null ? '' : String(value);
}

/**
 * Ensures `bpmn:extensionElements` and the typed Agentwerke extension element both
 * exist, then sets the given properties on the extension element. Creates the
 * containers lazily on first edit.
 */
export function setExtensionProperty(
  element: any,
  type: string,
  properties: Record<string, unknown>,
  { modeling, moddle }: ModdleServices,
): void {
  const bo = getBusinessObject(element);

  let extensionElements = bo.extensionElements;
  if (!extensionElements) {
    extensionElements = moddle.create('bpmn:ExtensionElements', { values: [] });
    extensionElements.$parent = bo;
    modeling.updateModdleProperties(element, bo, { extensionElements });
  }

  let extension = (extensionElements.values || []).find(
    (value: any) => value.$type === type,
  );
  if (!extension) {
    extension = moddle.create(type, {});
    extension.$parent = extensionElements;
    modeling.updateModdleProperties(element, extensionElements, {
      values: [...(extensionElements.values || []), extension],
    });
  }

  // Normalize empty strings to `undefined` so the attribute is removed from XML
  // rather than serialized as an empty value.
  const normalized: Record<string, unknown> = {};
  for (const [key, raw] of Object.entries(properties)) {
    normalized[key] = raw === '' ? undefined : raw;
  }

  modeling.updateModdleProperties(element, extension, normalized);
}

import { getBusinessObject } from 'bpmn-js/lib/util/ModelUtil';
import { AGENT_TASK_TYPE, METADATA_TYPE } from '../constants';

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Helpers for reading and writing Agentwerke extension elements
 * (`agentwerke:agentTask` / `agentwerke:approvalTask`) on a BPMN element's business
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

/**
 * Ensures the `agentwerke:agentTask` extension element exists (creating the
 * extensionElements container and agentTask lazily) and returns it. Used by the
 * metadata list editor, which needs the agentTask to hang `<agentwerke:metadata>`
 * children off of even before any attribute has been set.
 */
function ensureAgentTask(element: any, { modeling, moddle }: ModdleServices): any {
  const bo = getBusinessObject(element);

  let extensionElements = bo.extensionElements;
  if (!extensionElements) {
    extensionElements = moddle.create('bpmn:ExtensionElements', { values: [] });
    extensionElements.$parent = bo;
    modeling.updateModdleProperties(element, bo, { extensionElements });
  }

  let agentTask = (extensionElements.values || []).find(
    (value: any) => value.$type === AGENT_TASK_TYPE,
  );
  if (!agentTask) {
    agentTask = moddle.create(AGENT_TASK_TYPE, {});
    agentTask.$parent = extensionElements;
    modeling.updateModdleProperties(element, extensionElements, {
      values: [...(extensionElements.values || []), agentTask],
    });
  }

  return agentTask;
}

/** Ordered `<agentwerke:metadata>` items on the element's agent task (empty if none). */
export function getMetadataItems(element: any): any[] {
  const agentTask = getExtension(element, AGENT_TASK_TYPE);
  return agentTask?.metadata ?? [];
}

/** Appends a new empty metadata row and returns it, creating the agent task if needed. */
export function addMetadataItem(element: any, services: ModdleServices): any {
  const { modeling, moddle } = services;
  const agentTask = ensureAgentTask(element, services);
  const item = moddle.create(METADATA_TYPE, {});
  item.$parent = agentTask;
  modeling.updateModdleProperties(element, agentTask, {
    metadata: [...(agentTask.metadata || []), item],
  });
  return item;
}

/** Removes a metadata row from the element's agent task. */
export function removeMetadataItem(
  element: any,
  item: any,
  { modeling }: Pick<ModdleServices, 'modeling'>,
): void {
  const agentTask = getExtension(element, AGENT_TASK_TYPE);
  if (!agentTask) {
    return;
  }
  modeling.updateModdleProperties(element, agentTask, {
    metadata: (agentTask.metadata || []).filter((existing: any) => existing !== item),
  });
}

/** Sets `key` or `value` on a single metadata row; empty clears the attribute. */
export function updateMetadataItem(
  element: any,
  item: any,
  property: 'key' | 'value',
  value: string,
  { modeling }: Pick<ModdleServices, 'modeling'>,
): void {
  modeling.updateModdleProperties(element, item, {
    [property]: value === '' ? undefined : value,
  });
}

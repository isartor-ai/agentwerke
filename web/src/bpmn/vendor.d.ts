/**
 * Minimal type shims for the bpmn-js / diagram-js / properties-panel stack,
 * which ship without bundled TypeScript declarations. These are intentionally
 * loose (`any`) — they exist so the strict TS build resolves the imports, not
 * to provide full type-safety over the imperative diagram-js API.
 */

declare module 'bpmn-js/lib/Modeler' {
  export interface ModelerOptions {
    container?: HTMLElement | string;
    propertiesPanel?: { parent: HTMLElement | string };
    additionalModules?: unknown[];
    moddleExtensions?: Record<string, unknown>;
    keyboard?: { bindTo: Document | HTMLElement };
    [key: string]: unknown;
  }

  export default class BpmnModeler {
    constructor(options?: ModelerOptions);
    importXML(xml: string): Promise<{ warnings: unknown[] }>;
    saveXML(options?: { format?: boolean }): Promise<{ xml: string }>;
    saveSVG(): Promise<{ svg: string }>;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    on(event: string, callback: (event: any) => void): void;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    on(event: string, priority: number, callback: (event: any) => void): void;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    off(event: string, callback: (event: any) => void): void;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    get<T = any>(name: string): T;
    destroy(): void;
    clear(): void;
  }
}

declare module 'bpmn-js-properties-panel' {
  export const BpmnPropertiesPanelModule: unknown;
  export const BpmnPropertiesProviderModule: unknown;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export function useService(name: string): any;
}

declare module '@bpmn-io/properties-panel' {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export const TextFieldEntry: any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export const TextAreaEntry: any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export const SelectEntry: any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export const CheckboxEntry: any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export const NumberFieldEntry: any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export const Group: any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export function isTextFieldEntryEdited(node: any): boolean;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export function isTextAreaEntryEdited(node: any): boolean;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export function isSelectEntryEdited(node: any): boolean;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export function isNumberFieldEntryEdited(node: any): boolean;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export function isCheckboxEntryEdited(node: any): boolean;
}

declare module 'bpmn-js/lib/util/ModelUtil' {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export function is(element: any, type: string): boolean;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export function getBusinessObject(element: any): any;
}

declare module 'htm/preact' {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export const html: any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  export function render(...args: any[]): any;
}

// CSS side-effect imports from the bpmn-js / properties-panel packages.
declare module 'bpmn-js/dist/assets/*';
declare module '@bpmn-io/properties-panel/dist/assets/*';

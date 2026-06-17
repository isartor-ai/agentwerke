import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useRef,
} from 'react';
import Modeler from 'bpmn-js/lib/Modeler';
import {
  BpmnPropertiesPanelModule,
  BpmnPropertiesProviderModule,
} from 'bpmn-js-properties-panel';
import type { BpmnValidationError } from '../types';
import { autofacModdleDescriptor } from '../bpmn/autofacModdle';
import { autofacModules } from '../bpmn/autofacModule';
import { AUTOFAC_NS_PREFIX } from '../bpmn/constants';

import 'bpmn-js/dist/assets/diagram-js.css';
import 'bpmn-js/dist/assets/bpmn-js.css';
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn.css';
import '@bpmn-io/properties-panel/dist/assets/properties-panel.css';

const CHANGE_DEBOUNCE_MS = 300;
const VALIDATION_MARKER = 'autofac-validation-error';

export interface BpmnModelerHandle {
  /** Serializes the current diagram to formatted BPMN 2.0 XML. */
  getXML(): Promise<string>;
  /** Replaces the diagram with the given BPMN XML. */
  importXML(xml: string): Promise<void>;
}

export interface BpmnModelerProps {
  /** Initial diagram. Subsequent loads should go through the imperative handle. */
  initialXml?: string;
  /** Fired (debounced) whenever the diagram changes, with fresh XML. */
  onChange?: (xml: string) => void;
  /** Fired when a modeling/import error occurs. */
  onError?: (message: string) => void;
  /** Fired after BPMN XML is successfully imported into the canvas. */
  onImportSuccess?: () => void;
  /** Fired once the modeler instance is constructed and ready for imports. */
  onReady?: () => void;
  /** Backend validation errors to surface as inline markers/overlays. */
  validationErrors?: BpmnValidationError[];
  className?: string;
}

/**
 * Embeds the bpmn-js modeler (canvas + properties panel) and bridges its
 * imperative API to React. Diagram loads are driven through the ref so the
 * editor is never fought by React re-renders.
 */
export const BpmnModeler = forwardRef<BpmnModelerHandle, BpmnModelerProps>(
  function BpmnModeler(
    { initialXml, onChange, onError, onImportSuccess, onReady, validationErrors, className },
    ref,
  ) {
    const canvasRef = useRef<HTMLDivElement | null>(null);
    const panelRef = useRef<HTMLDivElement | null>(null);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const modelerRef = useRef<any>(null);
    const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    // Keep the latest callbacks in refs so the modeler effect runs once.
    const onChangeRef = useRef(onChange);
    const onErrorRef = useRef(onError);
    const onImportSuccessRef = useRef(onImportSuccess);
    const onReadyRef = useRef(onReady);
    onChangeRef.current = onChange;
    onErrorRef.current = onError;
    onImportSuccessRef.current = onImportSuccess;
    onReadyRef.current = onReady;

    useImperativeHandle(
      ref,
      () => ({
        async getXML() {
          if (!modelerRef.current) {
            return '';
          }
          const { xml } = await modelerRef.current.saveXML({ format: true });
          return xml ?? '';
        },
        async importXML(xml: string) {
          if (!modelerRef.current || !xml.trim()) {
            return;
          }
          try {
            await modelerRef.current.importXML(xml);
            modelerRef.current.get('canvas').zoom('fit-viewport');
            onImportSuccessRef.current?.();
          } catch (error) {
            onErrorRef.current?.(
              error instanceof Error ? error.message : 'Failed to render BPMN diagram.',
            );
          }
        },
      }),
      [],
    );

    // Instantiate the modeler once on mount.
    useEffect(() => {
      if (!canvasRef.current || !panelRef.current) {
        return;
      }

      let disposed = false;
      const modeler = new Modeler({
        container: canvasRef.current,
        propertiesPanel: { parent: panelRef.current },
        additionalModules: [
          BpmnPropertiesPanelModule,
          BpmnPropertiesProviderModule,
          ...autofacModules,
        ],
        moddleExtensions: {
          [AUTOFAC_NS_PREFIX]: autofacModdleDescriptor,
        },
        keyboard: { bindTo: document },
      });
      modelerRef.current = modeler;

      const emitChange = () => {
        if (debounceRef.current) {
          clearTimeout(debounceRef.current);
        }
        debounceRef.current = setTimeout(async () => {
          try {
            const { xml } = await modeler.saveXML({ format: true });
            onChangeRef.current?.(xml ?? '');
          } catch {
            // Ignore transient serialization errors mid-edit.
          }
        }, CHANGE_DEBOUNCE_MS);
      };

      modeler.on('commandStack.changed', emitChange);

      const seed = initialXml && initialXml.trim() ? initialXml : undefined;
      if (seed) {
        modeler
          .importXML(seed)
          .then(() => {
            if (disposed) {
              return;
            }
            modeler.get('canvas').zoom('fit-viewport');
            onImportSuccessRef.current?.();
          })
          .catch((error: unknown) => {
            if (disposed) {
              return;
            }
            onErrorRef.current?.(
              error instanceof Error ? error.message : 'Failed to render BPMN diagram.',
            );
          });
      }

      onReadyRef.current?.();

      return () => {
        disposed = true;
        if (debounceRef.current) {
          clearTimeout(debounceRef.current);
        }
        modeler.destroy();
        modelerRef.current = null;
      };
      // Mount-only: initialXml is a seed; later loads use the imperative handle.
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // Reflect backend validation errors as inline markers + overlays.
    useEffect(() => {
      const modeler = modelerRef.current;
      if (!modeler) {
        return;
      }

      let overlays: any; // eslint-disable-line @typescript-eslint/no-explicit-any
      let elementRegistry: any; // eslint-disable-line @typescript-eslint/no-explicit-any
      let canvas: any; // eslint-disable-line @typescript-eslint/no-explicit-any
      try {
        overlays = modeler.get('overlays');
        elementRegistry = modeler.get('elementRegistry');
        canvas = modeler.get('canvas');
      } catch {
        return;
      }

      overlays.clear();
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      elementRegistry.forEach((element: any) => {
        canvas.removeMarker(element, VALIDATION_MARKER);
      });

      for (const issue of validationErrors ?? []) {
        if (!issue.elementId) {
          continue;
        }
        const element = elementRegistry.get(issue.elementId);
        if (!element) {
          continue;
        }
        canvas.addMarker(element, VALIDATION_MARKER);
        const badge = document.createElement('div');
        badge.className = 'autofac-validation-overlay';
        badge.textContent = issue.message;
        overlays.add(issue.elementId, {
          position: { bottom: -6, left: 0 },
          html: badge,
        });
      }
    }, [validationErrors]);

    return (
      <div className={`bpmn-modeler ${className ?? ''}`.trim()}>
        <div className="bpmn-modeler-canvas" ref={canvasRef} aria-label="BPMN canvas" />
        <div className="bpmn-modeler-panel" ref={panelRef} aria-label="BPMN properties" />
      </div>
    );
  },
);

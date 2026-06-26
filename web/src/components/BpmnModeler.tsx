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
import { ensureDiagramInterchange } from '../bpmn/layout';
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
  /**
   * When true, the modeler is configured for Camunda-compatible editing:
   * Camunda-specific moddle extensions and properties providers will be loaded
   * when available. Defaults to false (Autofac default runtime only).
   */
  camundaMode?: boolean;
  className?: string;
}

/**
 * Embeds the bpmn-js modeler (canvas + properties panel) and bridges its
 * imperative API to React. Diagram loads are driven through the ref so the
 * editor is never fought by React re-renders.
 */
export const BpmnModeler = forwardRef<BpmnModelerHandle, BpmnModelerProps>(
  function BpmnModeler(
    { initialXml, onChange, onError, onImportSuccess, onReady, validationErrors, camundaMode = false, className },
    ref,
  ) {
    const canvasRef = useRef<HTMLDivElement | null>(null);
    const panelRef = useRef<HTMLDivElement | null>(null);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const modelerRef = useRef<any>(null);
    const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const importRevisionRef = useRef(0);
    const importQueueRef = useRef<Promise<void>>(Promise.resolve());

    // Keep the latest callbacks in refs so the modeler effect runs once.
    const onChangeRef = useRef(onChange);
    const onErrorRef = useRef(onError);
    const onImportSuccessRef = useRef(onImportSuccess);
    const onReadyRef = useRef(onReady);
    onChangeRef.current = onChange;
    onErrorRef.current = onError;
    onImportSuccessRef.current = onImportSuccess;
    onReadyRef.current = onReady;

    const importIntoModeler = (xml: string): Promise<void> => {
      const revision = importRevisionRef.current + 1;
      importRevisionRef.current = revision;

      const importTask = importQueueRef.current
        .catch(() => {
          // Keep the queue alive after a failed import; the failure is surfaced
          // below through onError and the next import should still run.
        })
        .then(async () => {
          const modeler = modelerRef.current;
          if (!modeler || !xml.trim() || revision !== importRevisionRef.current) {
            return;
          }

          try {
            const xmlWithLayout = await ensureDiagramInterchange(xml);
            if (revision !== importRevisionRef.current || modeler !== modelerRef.current) {
              return;
            }

            modeler.clear();
            await modeler.importXML(xmlWithLayout);
            if (revision !== importRevisionRef.current || modeler !== modelerRef.current) {
              return;
            }

            modeler.get('canvas').zoom('fit-viewport');
            onImportSuccessRef.current?.();
          } catch (error) {
            if (revision !== importRevisionRef.current || modeler !== modelerRef.current) {
              return;
            }

            try {
              modeler.clear();
            } catch {
              // Best effort: a failed bpmn-js import may leave internals half-built.
            }
            onErrorRef.current?.(
              error instanceof Error ? error.message : 'Failed to render BPMN diagram.',
            );
          }
        });

      importQueueRef.current = importTask.catch(() => {});
      return importTask;
    };

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
          await importIntoModeler(xml);
        },
      }),
      [],
    );

    // Captured at mount; camundaMode is a construction-time config, not reactive.
    const camundaModeRef = useRef(camundaMode);

    // Instantiate the modeler once on mount.
    useEffect(() => {
      if (!canvasRef.current || !panelRef.current) {
        return;
      }

      // Camunda-specific providers (e.g. CamundaPropertiesProviderModule) would be
      // appended here when camundaModeRef.current is true and the camunda-bpmn-js
      // package is present. Currently a no-op; the hook is in place for the adapter.
      void camundaModeRef.current;

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
        void importIntoModeler(seed);
      }

      onReadyRef.current?.();

      return () => {
        if (debounceRef.current) {
          clearTimeout(debounceRef.current);
        }
        importRevisionRef.current += 1;
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

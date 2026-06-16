import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';
import type { BpmnModelerHandle, BpmnModelerProps } from '../BpmnModeler';

/**
 * Test double for the bpmn-js-backed <BpmnModeler />. The real component
 * cannot render in jsdom (diagram-js needs SVG layout), so tests substitute
 * this lightweight stub that honours the same imperative handle: `getXML`
 * returns the last imported XML and `importXML` records it + fires `onChange`.
 */
export const BpmnModeler = forwardRef<BpmnModelerHandle, BpmnModelerProps>(
  function BpmnModelerMock(props, ref) {
    const [xml, setXml] = useState('');
    const xmlRef = useRef('');
    const onReadyRef = useRef(props.onReady);
    onReadyRef.current = props.onReady;

    useImperativeHandle(ref, () => ({
      getXML: async () => xmlRef.current,
      importXML: async (next: string) => {
        xmlRef.current = next;
        setXml(next);
        props.onChange?.(next);
      },
    }));

    useEffect(() => {
      onReadyRef.current?.();
    }, []);

    return (
      <div data-testid="bpmn-modeler-mock" aria-label="BPMN canvas">
        {xml}
      </div>
    );
  },
);

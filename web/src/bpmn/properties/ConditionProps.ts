import { html } from 'htm/preact';
import { TextFieldEntry, isTextFieldEntryEdited } from '@bpmn-io/properties-panel';
import { useService } from 'bpmn-js-properties-panel';
import { getBusinessObject, is } from 'bpmn-js/lib/util/ModelUtil';

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * True for sequence flows that leave an exclusive gateway — the only flows the
 * default runtime evaluates conditions on.
 */
export function isConditionalFlow(element: any): boolean {
  if (!is(element, 'bpmn:SequenceFlow')) {
    return false;
  }

  const source = getBusinessObject(element)?.sourceRef;
  return source?.$type === 'bpmn:ExclusiveGateway';
}

/**
 * Reads/writes the standard `bpmn:conditionExpression` (a FormalExpression) on
 * the flow. Clearing the field removes the expression, which makes the flow the
 * gateway's default branch.
 */
function conditionExpressionField() {
  return function ConditionExpressionField(props: any) {
    const { element, id } = props;
    const modeling = useService('modeling');
    const moddle = useService('moddle');
    const translate = useService('translate');
    const debounce = useService('debounceInput');

    const getValue = () => getBusinessObject(element)?.conditionExpression?.body ?? '';

    const setValue = (value: string) => {
      const expression = value?.trim()
        ? moddle.create('bpmn:FormalExpression', { body: value })
        : undefined;
      modeling.updateProperties(element, { conditionExpression: expression });
    };

    return html`<${TextFieldEntry}
      id=${id}
      element=${element}
      label=${translate('Condition expression')}
      description=${translate(
        'Operators: ==, !=, contains. Operands: "quoted string", {{output.<nodeId>}}, {{event.*}}, {{input.*}}. ' +
        'Example: {{output.RunTests}} contains "VERDICT: PASS". Leave empty on exactly one flow for the default branch.',
      )}
      getValue=${getValue}
      setValue=${setValue}
      debounce=${debounce}
      placeholder=${'e.g. {{output.RunTests}} contains "VERDICT: PASS"'}
    />`;
  };
}

export function conditionEntries(element: any) {
  return [
    {
      id: 'agentwerke-conditionExpression',
      component: conditionExpressionField(),
      isEdited: isTextFieldEntryEdited,
      element,
    },
  ];
}

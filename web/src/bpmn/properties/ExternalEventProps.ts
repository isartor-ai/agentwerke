import { html } from 'htm/preact';
import { TextFieldEntry, isTextFieldEntryEdited } from '@bpmn-io/properties-panel';
import { useService } from 'bpmn-js-properties-panel';
import { EXTERNAL_EVENT_TYPE } from '../constants';
import { getExtensionProperty, setExtensionProperty } from './extensionUtil';

/* eslint-disable @typescript-eslint/no-explicit-any */

function textField(attribute: string, label: string, placeholder?: string) {
  return function ExternalEventTextField(props: any) {
    const { element, id } = props;
    const modeling = useService('modeling');
    const moddle = useService('moddle');
    const translate = useService('translate');
    const debounce = useService('debounceInput');

    const getValue = () => getExtensionProperty(element, EXTERNAL_EVENT_TYPE, attribute);
    const setValue = (value: string) =>
      setExtensionProperty(element, EXTERNAL_EVENT_TYPE, { [attribute]: value }, { modeling, moddle });

    return html`<${TextFieldEntry}
      id=${id}
      element=${element}
      label=${translate(label)}
      getValue=${getValue}
      setValue=${setValue}
      debounce=${debounce}
      ...${placeholder ? { placeholder } : {}}
    />`;
  };
}

export function externalEventEntries(element: any) {
  return [
    {
      id: 'autofac-external-messageName',
      component: textField('messageName', 'Message name', 'e.g. github.pull_request.merged'),
      isEdited: isTextFieldEntryEdited,
      element,
    },
    {
      id: 'autofac-external-correlationKeyTemplate',
      component: textField('correlationKeyTemplate', 'Correlation key template', 'e.g. {{run_context.branch_name}}'),
      isEdited: isTextFieldEntryEdited,
      element,
    },
  ];
}

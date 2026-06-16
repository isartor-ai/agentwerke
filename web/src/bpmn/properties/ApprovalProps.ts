import { html } from 'htm/preact';
import { TextFieldEntry, isTextFieldEntryEdited } from '@bpmn-io/properties-panel';
import { useService } from 'bpmn-js-properties-panel';
import { APPROVAL_TASK_TYPE } from '../constants';
import { getExtensionProperty, setExtensionProperty } from './extensionUtil';

/* eslint-disable @typescript-eslint/no-explicit-any */

/** Builds a text entry bound to a single `autofac:approvalTask` attribute. */
function textField(attribute: string, label: string, placeholder?: string) {
  return function ApprovalTextField(props: any) {
    const { element, id } = props;
    const modeling = useService('modeling');
    const moddle = useService('moddle');
    const translate = useService('translate');
    const debounce = useService('debounceInput');

    const getValue = () => getExtensionProperty(element, APPROVAL_TASK_TYPE, attribute);
    const setValue = (value: string) =>
      setExtensionProperty(element, APPROVAL_TASK_TYPE, { [attribute]: value }, { modeling, moddle });

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

/**
 * Property entries for the "Approval Gate" group, shown for user tasks.
 * Mirrors the attributes the backend validator requires on
 * `autofac:approvalTask`.
 */
export function approvalEntries(element: any) {
  return [
    { id: 'autofac-approval-purposeType', component: textField('purposeType', 'Purpose type', 'e.g. production_deployment'), isEdited: isTextFieldEntryEdited, element },
    { id: 'autofac-approval-policyTag', component: textField('policyTag', 'Policy tag', 'e.g. deploy_approval'), isEdited: isTextFieldEntryEdited, element },
  ];
}

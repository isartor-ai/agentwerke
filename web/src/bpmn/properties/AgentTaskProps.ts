import { html } from 'htm/preact';
import {
  TextFieldEntry,
  isTextFieldEntryEdited,
  NumberFieldEntry,
  isNumberFieldEntryEdited,
} from '@bpmn-io/properties-panel';
import { useService } from 'bpmn-js-properties-panel';
import { AGENT_TASK_TYPE } from '../constants';
import { getExtensionProperty, setExtensionProperty } from './extensionUtil';

/* eslint-disable @typescript-eslint/no-explicit-any */

/** Builds a text entry bound to a single `autofac:agentTask` attribute. */
function textField(
  attribute: string,
  label: string,
  placeholder?: string,
) {
  return function AgentTaskTextField(props: any) {
    const { element, id } = props;
    const modeling = useService('modeling');
    const moddle = useService('moddle');
    const translate = useService('translate');
    const debounce = useService('debounceInput');

    const getValue = () => getExtensionProperty(element, AGENT_TASK_TYPE, attribute);
    const setValue = (value: string) =>
      setExtensionProperty(element, AGENT_TASK_TYPE, { [attribute]: value }, { modeling, moddle });

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

/** Builds a non-negative integer entry bound to an `autofac:agentTask` attribute. */
function numberField(attribute: string, label: string) {
  return function AgentTaskNumberField(props: any) {
    const { element, id } = props;
    const modeling = useService('modeling');
    const moddle = useService('moddle');
    const translate = useService('translate');
    const debounce = useService('debounceInput');

    const getValue = () => getExtensionProperty(element, AGENT_TASK_TYPE, attribute);
    const setValue = (value: string) =>
      setExtensionProperty(
        element,
        AGENT_TASK_TYPE,
        { [attribute]: value === '' || value === undefined ? undefined : Number(value) },
        { modeling, moddle },
      );

    return html`<${NumberFieldEntry}
      id=${id}
      element=${element}
      label=${translate(label)}
      getValue=${getValue}
      setValue=${setValue}
      debounce=${debounce}
      min=${0}
    />`;
  };
}

/**
 * Property entries for the "Agent Task" group, shown for service/script tasks.
 * Mirrors the attributes the backend `BpmnWorkflowValidator` requires.
 */
export function agentTaskEntries(element: any) {
  return [
    { id: 'autofac-agent', component: textField('agent', 'Agent', 'e.g. DeployAgent'), isEdited: isTextFieldEntryEdited, element },
    { id: 'autofac-action', component: textField('action', 'Action', 'e.g. cloud.deploy_artifact'), isEdited: isTextFieldEntryEdited, element },
    { id: 'autofac-environment', component: textField('environment', 'Environment', 'e.g. production'), isEdited: isTextFieldEntryEdited, element },
    { id: 'autofac-purposeType', component: textField('purposeType', 'Purpose type', 'e.g. production_deployment'), isEdited: isTextFieldEntryEdited, element },
    { id: 'autofac-policyTag', component: textField('policyTag', 'Policy tag', 'e.g. deploy_gateway'), isEdited: isTextFieldEntryEdited, element },
    { id: 'autofac-requiresEvidence', component: textField('requiresEvidence', 'Required evidence', 'comma-separated'), isEdited: isTextFieldEntryEdited, element },
    { id: 'autofac-maxRetries', component: numberField('maxRetries', 'Max retries'), isEdited: isNumberFieldEntryEdited, element },
    { id: 'autofac-retryBackoffSeconds', component: numberField('retryBackoffSeconds', 'Retry backoff (s)'), isEdited: isNumberFieldEntryEdited, element },
    { id: 'autofac-timeoutSeconds', component: numberField('timeoutSeconds', 'Timeout (s)'), isEdited: isNumberFieldEntryEdited, element },
  ];
}

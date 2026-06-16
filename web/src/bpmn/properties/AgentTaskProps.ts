import { html } from 'htm/preact';
import {
  TextFieldEntry,
  isTextFieldEntryEdited,
  NumberFieldEntry,
  isNumberFieldEntryEdited,
  SelectEntry,
  isSelectEntryEdited,
} from '@bpmn-io/properties-panel';
import { useService } from 'bpmn-js-properties-panel';
import { AGENT_TASK_TYPE } from '../constants';
import { getAgentCatalog } from '../agentCatalog';
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

/**
 * Builds the Agent drop-down, populated from the registered-agent catalog
 * (`GET /api/agents`). The stored value is preserved even if it isn't a
 * registered agent — it appears as a "(custom)" option so existing diagrams and
 * not-yet-registered agents still work.
 */
function agentSelectField() {
  return function AgentSelect(props: any) {
    const { element, id } = props;
    const modeling = useService('modeling');
    const moddle = useService('moddle');
    const translate = useService('translate');

    const getValue = () => getExtensionProperty(element, AGENT_TASK_TYPE, 'agent') ?? '';
    const setValue = (value: string) =>
      setExtensionProperty(element, AGENT_TASK_TYPE, { agent: value || undefined }, { modeling, moddle });

    const getOptions = () => {
      const current = getValue();
      const agents = getAgentCatalog();
      const options: { value: string; label: string }[] = [{ value: '', label: '— select agent —' }];

      for (const agent of agents) {
        const suffix = agent.runner === 'claude-code' ? ' · sandbox' : '';
        options.push({ value: agent.agentId, label: `${agent.name} (${agent.agentId})${suffix}` });
      }

      // Preserve a stored value that isn't in the catalog (custom / unregistered).
      if (current && !agents.some((a) => a.agentId === current)) {
        options.push({ value: current, label: `${current} (custom)` });
      }

      return options;
    };

    return html`<${SelectEntry}
      id=${id}
      element=${element}
      label=${translate('Agent')}
      getValue=${getValue}
      setValue=${setValue}
      getOptions=${getOptions}
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
    { id: 'autofac-agent', component: agentSelectField(), isEdited: isSelectEntryEdited, element },
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

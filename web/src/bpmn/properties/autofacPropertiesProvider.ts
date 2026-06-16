import { is } from 'bpmn-js/lib/util/ModelUtil';
import { Group } from '@bpmn-io/properties-panel';
import { agentTaskEntries } from './AgentTaskProps';
import { approvalEntries } from './ApprovalProps';

/* eslint-disable @typescript-eslint/no-explicit-any */

const LOW_PRIORITY = 500;

/**
 * Properties-panel provider that injects Autofac groups:
 *  - "Agent Task" for service/script tasks
 *  - "Approval Gate" for user tasks
 *
 * Registered as a diagram-js module (see `autofacPropertiesModule.ts`).
 */
function AutofacPropertiesProvider(
  this: any,
  propertiesPanel: any,
  translate: any,
) {
  this.getGroups = function (element: any) {
    return function (groups: any[]) {
      if (is(element, 'bpmn:ServiceTask') || is(element, 'bpmn:ScriptTask')) {
        groups.push({
          id: 'autofacAgentTask',
          label: translate('Autofac — Agent Task'),
          entries: agentTaskEntries(element),
          component: Group,
        });
      }

      if (is(element, 'bpmn:UserTask')) {
        groups.push({
          id: 'autofacApproval',
          label: translate('Autofac — Approval Gate'),
          entries: approvalEntries(element),
          component: Group,
        });
      }

      return groups;
    };
  };

  propertiesPanel.registerProvider(LOW_PRIORITY, this);
}

AutofacPropertiesProvider.$inject = ['propertiesPanel', 'translate'];

export const AutofacPropertiesProviderModule = {
  __init__: ['autofacPropertiesProvider'],
  autofacPropertiesProvider: ['type', AutofacPropertiesProvider],
};

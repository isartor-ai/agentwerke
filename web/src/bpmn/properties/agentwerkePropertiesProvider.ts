import { getBusinessObject, is } from 'bpmn-js/lib/util/ModelUtil';
import { Group } from '@bpmn-io/properties-panel';
import { agentTaskEntries } from './AgentTaskProps';
import { approvalEntries } from './ApprovalProps';
import { externalEventEntries } from './ExternalEventProps';

/* eslint-disable @typescript-eslint/no-explicit-any */

const LOW_PRIORITY = 500;

function isMessageCatchEvent(element: any) {
  if (!is(element, 'bpmn:IntermediateCatchEvent')) {
    return false;
  }

  const businessObject = getBusinessObject(element);
  return (businessObject?.eventDefinitions ?? []).some(
    (definition: any) => definition?.$type === 'bpmn:MessageEventDefinition',
  );
}

/**
 * Properties-panel provider that injects Agentwerke governance groups:
 *  - "Agent Task" for service/script tasks
 *  - "Approval Gate" for user tasks
 *
 * Registered as a diagram-js module (see `agentwerkePropertiesModule.ts`).
 */
function AgentwerkePropertiesProvider(
  this: any,
  propertiesPanel: any,
  translate: any,
) {
  this.getGroups = function (element: any) {
    return function (groups: any[]) {
      if (is(element, 'bpmn:ServiceTask') || is(element, 'bpmn:ScriptTask')) {
        groups.push({
          id: 'agentwerkeAgentTask',
          label: translate('Agentwerke — Agent Task'),
          entries: agentTaskEntries(element),
          component: Group,
        });
      }

      if (is(element, 'bpmn:UserTask')) {
        groups.push({
          id: 'agentwerkeApproval',
          label: translate('Agentwerke — Approval Gate'),
          entries: approvalEntries(element),
          component: Group,
        });
      }

      if (is(element, 'bpmn:ReceiveTask') || isMessageCatchEvent(element)) {
        groups.push({
          id: 'agentwerkeExternalEvent',
          label: translate('Agentwerke — External Wait'),
          entries: externalEventEntries(element),
          component: Group,
        });
      }

      return groups;
    };
  };

  propertiesPanel.registerProvider(LOW_PRIORITY, this);
}

AgentwerkePropertiesProvider.$inject = ['propertiesPanel', 'translate'];

export const AgentwerkePropertiesProviderModule = {
  __init__: ['agentwerkePropertiesProvider'],
  agentwerkePropertiesProvider: ['type', AgentwerkePropertiesProvider],
};

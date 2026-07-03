import { getExtension } from './properties/extensionUtil';
import { AGENT_TASK_TYPE, APPROVAL_TASK_TYPE } from './constants';

/* eslint-disable @typescript-eslint/no-explicit-any */

const AGENT_MARKER = 'agentwerke-agent-task';
const APPROVAL_MARKER = 'agentwerke-approval-task';

/**
 * Tags shapes that carry Agentwerke extension metadata with marker CSS classes
 * (`agentwerke-agent-task` / `agentwerke-approval-task`) so the canvas can render a
 * distinguishing accent. Re-evaluated after import and on every model change.
 */
function AgentwerkeMarkers(
  this: any,
  eventBus: any,
  canvas: any,
  elementRegistry: any,
) {
  const refresh = () => {
    elementRegistry.forEach((element: any) => {
      if (!element || !element.businessObject) {
        return;
      }

      const hasAgent = Boolean(getExtension(element, AGENT_TASK_TYPE));
      const hasApproval = Boolean(getExtension(element, APPROVAL_TASK_TYPE));

      canvas.removeMarker(element, AGENT_MARKER);
      canvas.removeMarker(element, APPROVAL_MARKER);

      if (hasAgent) {
        canvas.addMarker(element, AGENT_MARKER);
      }
      if (hasApproval) {
        canvas.addMarker(element, APPROVAL_MARKER);
      }
    });
  };

  eventBus.on('import.done', refresh);
  eventBus.on('commandStack.changed', refresh);
}

AgentwerkeMarkers.$inject = ['eventBus', 'canvas', 'elementRegistry'];

export const AgentwerkeMarkersModule = {
  __init__: ['agentwerkeMarkers'],
  agentwerkeMarkers: ['type', AgentwerkeMarkers],
};

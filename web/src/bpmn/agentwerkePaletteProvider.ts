import { AGENT_TASK_TYPE, APPROVAL_TASK_TYPE, EXTERNAL_EVENT_TYPE } from './constants';

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Adds first-class Agentwerke entries to the editor palette so users drag
 * governed agent and approval tasks directly onto BPMN diagrams.
 * concepts rather than raw BPMN:
 *  - "Agent Task"  → bpmn:ServiceTask pre-stamped with agentwerke:agentTask
 *  - "Approval Gate" → bpmn:UserTask pre-stamped with agentwerke:approvalTask
 */
function AgentwerkePaletteProvider(
  this: any,
  palette: any,
  create: any,
  elementFactory: any,
  bpmnFactory: any,
  translate: any,
) {
  this._create = create;
  this._elementFactory = elementFactory;
  this._bpmnFactory = bpmnFactory;
  this._translate = translate;

  palette.registerProvider(this);
}

AgentwerkePaletteProvider.$inject = [
  'palette',
  'create',
  'elementFactory',
  'bpmnFactory',
  'translate',
];

AgentwerkePaletteProvider.prototype.getPaletteEntries = function () {
  const create = this._create;
  const elementFactory = this._elementFactory;
  const bpmnFactory = this._bpmnFactory;
  const translate = this._translate;

  const buildTask = (bpmnType: string, extensionType: string, defaults: Record<string, unknown>) => {
    const extension = bpmnFactory.create(extensionType, defaults);
    const extensionElements = bpmnFactory.create('bpmn:ExtensionElements', {
      values: [extension],
    });
    extension.$parent = extensionElements;

    const businessObject = bpmnFactory.create(bpmnType, { extensionElements });
    extensionElements.$parent = businessObject;

    return elementFactory.createShape({ type: bpmnType, businessObject });
  };

  const buildExternalWaitEvent = () => {
    const extension = bpmnFactory.create(EXTERNAL_EVENT_TYPE, {
      messageName: 'external.event',
      correlationKeyTemplate: '{{run_context.correlation_key}}',
    });
    const extensionElements = bpmnFactory.create('bpmn:ExtensionElements', {
      values: [extension],
    });
    extension.$parent = extensionElements;

    const messageEventDefinition = bpmnFactory.create('bpmn:MessageEventDefinition');
    const businessObject = bpmnFactory.create('bpmn:IntermediateCatchEvent', {
      eventDefinitions: [messageEventDefinition],
      extensionElements,
    });

    extensionElements.$parent = businessObject;
    messageEventDefinition.$parent = businessObject;

    return elementFactory.createShape({
      type: 'bpmn:IntermediateCatchEvent',
      businessObject,
    });
  };

  const buildReceiveTask = () => {
    const extension = bpmnFactory.create(EXTERNAL_EVENT_TYPE, {
      messageName: 'external.event',
      correlationKeyTemplate: '{{run_context.correlation_key}}',
    });
    const extensionElements = bpmnFactory.create('bpmn:ExtensionElements', {
      values: [extension],
    });
    extension.$parent = extensionElements;

    const businessObject = bpmnFactory.create('bpmn:ReceiveTask', { extensionElements });
    extensionElements.$parent = businessObject;

    return elementFactory.createShape({
      type: 'bpmn:ReceiveTask',
      businessObject,
    });
  };

  const startAgentTask = (event: any) => {
    const shape = buildTask('bpmn:ServiceTask', AGENT_TASK_TYPE, {
      agent: 'AgentWorker',
      action: 'task.execute',
      environment: 'staging',
      purposeType: 'build_release',
      policyTag: 'task_policy',
    });
    create.start(event, shape);
  };

  const startApprovalTask = (event: any) => {
    const shape = buildTask('bpmn:UserTask', APPROVAL_TASK_TYPE, {
      purposeType: 'production_deployment',
      policyTag: 'approval_required',
    });
    create.start(event, shape);
  };

  const startExternalWait = (event: any) => {
    create.start(event, buildExternalWaitEvent());
  };

  const startReceiveTask = (event: any) => {
    create.start(event, buildReceiveTask());
  };

  return {
    'agentwerke-separator': {
      group: 'agentwerke',
      separator: true,
    },
    'agentwerke-agent-task': {
      group: 'agentwerke',
      className: 'bpmn-icon-service-task agentwerke-palette-agent',
      title: translate('Create Agent Task'),
      action: {
        dragstart: startAgentTask,
        click: startAgentTask,
      },
    },
    'agentwerke-approval-task': {
      group: 'agentwerke',
      className: 'bpmn-icon-user-task agentwerke-palette-approval',
      title: translate('Create Approval Gate'),
      action: {
        dragstart: startApprovalTask,
        click: startApprovalTask,
      },
    },
    'agentwerke-external-wait': {
      group: 'agentwerke',
      className: 'bpmn-icon-intermediate-event-catch-message',
      title: translate('Create External Wait'),
      action: {
        dragstart: startExternalWait,
        click: startExternalWait,
      },
    },
    'agentwerke-receive-task': {
      group: 'agentwerke',
      className: 'bpmn-icon-receive-task',
      title: translate('Create Receive Task'),
      action: {
        dragstart: startReceiveTask,
        click: startReceiveTask,
      },
    },
  };
};

export const AgentwerkePaletteModule = {
  __init__: ['agentwerkePaletteProvider'],
  agentwerkePaletteProvider: ['type', AgentwerkePaletteProvider],
};

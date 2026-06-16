import { AGENT_TASK_TYPE, APPROVAL_TASK_TYPE } from './constants';

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Adds first-class Autofac entries to the editor palette so users drag Autofac
 * concepts rather than raw BPMN:
 *  - "Agent Task"  → bpmn:ServiceTask pre-stamped with autofac:agentTask
 *  - "Approval Gate" → bpmn:UserTask pre-stamped with autofac:approvalTask
 */
function AutofacPaletteProvider(
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

AutofacPaletteProvider.$inject = [
  'palette',
  'create',
  'elementFactory',
  'bpmnFactory',
  'translate',
];

AutofacPaletteProvider.prototype.getPaletteEntries = function () {
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

  return {
    'autofac-separator': {
      group: 'autofac',
      separator: true,
    },
    'autofac-agent-task': {
      group: 'autofac',
      className: 'bpmn-icon-service-task autofac-palette-agent',
      title: translate('Create Agent Task'),
      action: {
        dragstart: startAgentTask,
        click: startAgentTask,
      },
    },
    'autofac-approval-task': {
      group: 'autofac',
      className: 'bpmn-icon-user-task autofac-palette-approval',
      title: translate('Create Approval Gate'),
      action: {
        dragstart: startApprovalTask,
        click: startApprovalTask,
      },
    },
  };
};

export const AutofacPaletteModule = {
  __init__: ['autofacPaletteProvider'],
  autofacPaletteProvider: ['type', AutofacPaletteProvider],
};

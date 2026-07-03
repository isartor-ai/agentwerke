import { AUTOFAC_NS_PREFIX, AUTOFAC_NS_URI } from './constants';

/**
 * Moddle descriptor for the Agentwerke BPMN extension namespace.
 *
 * Registering this with the modeler (`moddleExtensions: { autofac: ... }`) lets
 * bpmn-js read and write `autofac:agentTask` / `autofac:approvalTask` extension
 * elements natively, so designer metadata is persisted inside the BPMN XML
 * rather than in a side channel.
 *
 * `xml.tagAlias: 'lowerCase'` makes the `AgentTask` type serialize as
 * `<autofac:agentTask>` — matching the lower-camel element names the backend
 * validator looks for.
 */
export const autofacModdleDescriptor = {
  name: 'Agentwerke',
  uri: AUTOFAC_NS_URI,
  prefix: AUTOFAC_NS_PREFIX,
  xml: {
    tagAlias: 'lowerCase',
  },
  associations: [],
  types: [
    {
      name: 'AgentTask',
      superClass: ['Element'],
      properties: [
        { name: 'agent', isAttr: true, type: 'String' },
        { name: 'action', isAttr: true, type: 'String' },
        { name: 'environment', isAttr: true, type: 'String' },
        { name: 'purposeType', isAttr: true, type: 'String' },
        { name: 'policyTag', isAttr: true, type: 'String' },
        { name: 'executionMode', isAttr: true, type: 'String' },
        { name: 'sandboxProfile', isAttr: true, type: 'String' },
        { name: 'permissionLevel', isAttr: true, type: 'String' },
        { name: 'allowedTools', isAttr: true, type: 'String' },
        { name: 'deniedTools', isAttr: true, type: 'String' },
        { name: 'requiresEvidence', isAttr: true, type: 'String' },
        { name: 'maxRetries', isAttr: true, type: 'Integer' },
        { name: 'retryBackoffSeconds', isAttr: true, type: 'Integer' },
        { name: 'timeoutSeconds', isAttr: true, type: 'Integer' },
      ],
    },
    {
      name: 'ApprovalTask',
      superClass: ['Element'],
      properties: [
        { name: 'purposeType', isAttr: true, type: 'String' },
        { name: 'policyTag', isAttr: true, type: 'String' },
      ],
    },
    {
      name: 'ExternalEvent',
      superClass: ['Element'],
      properties: [
        { name: 'messageName', isAttr: true, type: 'String' },
        { name: 'correlationKeyTemplate', isAttr: true, type: 'String' },
      ],
    },
  ],
};

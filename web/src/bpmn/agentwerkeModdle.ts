import { AGENTWERKE_NS_PREFIX, AGENTWERKE_NS_URI } from './constants';

/**
 * Moddle descriptor for the Agentwerke BPMN extension namespace.
 *
 * Registering this with the modeler (`moddleExtensions: { agentwerke: ... }`) lets
 * bpmn-js read and write `agentwerke:agentTask` / `agentwerke:approvalTask` extension
 * elements natively, so designer metadata is persisted inside the BPMN XML
 * rather than in a side channel.
 *
 * `xml.tagAlias: 'lowerCase'` makes the `AgentTask` type serialize as
 * `<agentwerke:agentTask>` — matching the lower-camel element names the backend
 * validator looks for.
 *
 * The `Metadata` and `Prompt` child types are load-bearing: bpmn-js only preserves
 * extension children it can model, so without these the `<agentwerke:metadata>` and
 * `<agentwerke:prompt>` elements are silently dropped on the first import→export,
 * corrupting authored workflows.
 */
export const agentwerkeModdleDescriptor = {
  name: 'Agentwerke',
  uri: AGENTWERKE_NS_URI,
  prefix: AGENTWERKE_NS_PREFIX,
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
        { name: 'toolEscalation', isAttr: true, type: 'String' },
        { name: 'requiresEvidence', isAttr: true, type: 'String' },
        { name: 'maxRetries', isAttr: true, type: 'Integer' },
        { name: 'retryBackoffSeconds', isAttr: true, type: 'Integer' },
        { name: 'timeoutSeconds', isAttr: true, type: 'Integer' },
        // Child elements. Order matters for serialization; keep after the attributes.
        { name: 'metadata', isMany: true, type: 'Metadata' },
        { name: 'prompt', type: 'Prompt' },
      ],
    },
    {
      name: 'Metadata',
      superClass: ['Element'],
      properties: [
        { name: 'key', isAttr: true, type: 'String' },
        { name: 'value', isAttr: true, type: 'String' },
      ],
    },
    {
      name: 'Prompt',
      superClass: ['Element'],
      properties: [
        { name: 'body', isBody: true, type: 'String' },
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

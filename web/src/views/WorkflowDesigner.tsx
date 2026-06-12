import { useEffect, useMemo, useRef, useState, type ChangeEvent } from 'react';
import { apiClient } from '../api/client';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import { Toolbar } from '../components/Toolbar';
import type { Workflow, WorkflowValidationResult } from '../types';

// localStorage keys
const STORAGE_KEYS = {
  BPMN_XML: 'autofac_draft_bpmn_xml',
  NODE_METADATA: 'autofac_draft_node_metadata',
  SELECTED_NODE: 'autofac_selected_node',
  LAST_PUBLISHED: 'autofac_last_published_xml',
} as const;

const AUTO_SAVE_DELAY_MS = 300;

const inspectorTabs = ['Details', 'Inputs', 'Policy', 'Agent', 'Retry'];

type BpmnNodeType =
  | 'startEvent'
  | 'endEvent'
  | 'serviceTask'
  | 'scriptTask'
  | 'userTask'
  | 'exclusiveGateway'
  | 'parallelGateway'
  | 'subProcess';

interface BpmnNodeCard {
  id: string;
  name: string;
  type: BpmnNodeType;
}

interface NodeMetadataDraft {
  agent: string;
  action: string;
  environment: string;
  purposeType: string;
  policyTag: string;
}

interface DiffLine {
  lineNumber: number;
  kind: 'added' | 'removed' | 'unchanged';
  text: string;
}

interface BpmnTemplate {
  id: string;
  name: string;
  description: string;
  preview: string[];
  xml: string;
}

const BPMN_TEMPLATE_LIBRARY: BpmnTemplate[] = [
  {
    id: 'tpl-deploy',
    name: 'Production Deploy Gate',
    description: 'Build, verify, human approval, and deploy to production.',
    preview: ['Start', 'Build', 'Security Scan', 'Approval', 'Deploy', 'End'],
    xml: `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:autofac="https://autofac.dev/bpmn/extensions/v1" id="Defs_Deploy" targetNamespace="https://autofac.dev/bpmn/extensions/v1">
  <bpmn:process id="ProductionDeploy" name="Production Deploy" isExecutable="true">
    <bpmn:startEvent id="StartEvent_1" name="Start" />
    <bpmn:serviceTask id="BuildTask" name="Build Artifact">
      <bpmn:extensionElements>
        <autofac:agentTask agent="BuildAgent" action="ci.build_artifact" environment="build" purposeType="build_release" policyTag="build_gateway" />
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:userTask id="ApprovalTask" name="Release Approval">
      <bpmn:extensionElements>
        <autofac:approvalTask purposeType="production_deployment" policyTag="deploy_approval" />
      </bpmn:extensionElements>
    </bpmn:userTask>
    <bpmn:serviceTask id="DeployTask" name="Deploy to Production">
      <bpmn:extensionElements>
        <autofac:agentTask agent="DeployAgent" action="cloud.deploy_artifact" environment="production" purposeType="production_deployment" policyTag="deploy_gateway" />
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:endEvent id="EndEvent_1" name="Completed" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="BuildTask" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="BuildTask" targetRef="ApprovalTask" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="ApprovalTask" targetRef="DeployTask" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="DeployTask" targetRef="EndEvent_1" />
  </bpmn:process>
</bpmn:definitions>`,
  },
  {
    id: 'tpl-ci',
    name: 'CI Approval Pipeline',
    description: 'Run tests and require human approval before merge.',
    preview: ['Start', 'Test', 'SAST', 'Approval', 'Merge', 'End'],
    xml: `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:autofac="https://autofac.dev/bpmn/extensions/v1" id="Defs_CI" targetNamespace="https://autofac.dev/bpmn/extensions/v1">
  <bpmn:process id="CiApproval" name="CI Approval" isExecutable="true">
    <bpmn:startEvent id="StartEvent_1" name="Start" />
    <bpmn:serviceTask id="TestTask" name="Run Tests" />
    <bpmn:serviceTask id="ScanTask" name="SAST Scan" />
    <bpmn:userTask id="ApprovalTask" name="Merge Approval" />
    <bpmn:serviceTask id="MergeTask" name="Merge PR" />
    <bpmn:endEvent id="EndEvent_1" name="Done" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="TestTask" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="TestTask" targetRef="ScanTask" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="ScanTask" targetRef="ApprovalTask" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="ApprovalTask" targetRef="MergeTask" />
    <bpmn:sequenceFlow id="Flow_5" sourceRef="MergeTask" targetRef="EndEvent_1" />
  </bpmn:process>
</bpmn:definitions>`,
  },
  {
    id: 'tpl-hotfix',
    name: 'Hotfix Response',
    description: 'Fast-track patch with risk controls and post-checks.',
    preview: ['Start', 'Patch', 'Approval', 'Deploy', 'Verify', 'End'],
    xml: `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Defs_Hotfix" targetNamespace="https://autofac.dev/bpmn/extensions/v1">
  <bpmn:process id="HotfixFlow" name="Hotfix Response" isExecutable="true">
    <bpmn:startEvent id="StartEvent_1" name="Incident Trigger" />
    <bpmn:serviceTask id="PatchTask" name="Generate Patch" />
    <bpmn:userTask id="ApprovalTask" name="SRE Approval" />
    <bpmn:serviceTask id="DeployTask" name="Deploy Hotfix" />
    <bpmn:serviceTask id="VerifyTask" name="Verify Health" />
    <bpmn:endEvent id="EndEvent_1" name="Resolved" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="PatchTask" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="PatchTask" targetRef="ApprovalTask" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="ApprovalTask" targetRef="DeployTask" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="DeployTask" targetRef="VerifyTask" />
    <bpmn:sequenceFlow id="Flow_5" sourceRef="VerifyTask" targetRef="EndEvent_1" />
  </bpmn:process>
</bpmn:definitions>`,
  },
  {
    id: 'tpl-feature',
    name: 'Feature Branch Delivery',
    description: 'Implement feature, run checks, and open pull request.',
    preview: ['Start', 'Code', 'Test', 'PR', 'Review', 'End'],
    xml: `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Defs_Feature" targetNamespace="https://autofac.dev/bpmn/extensions/v1">
  <bpmn:process id="FeatureDelivery" name="Feature Delivery" isExecutable="true">
    <bpmn:startEvent id="StartEvent_1" name="Start" />
    <bpmn:serviceTask id="ImplementTask" name="Implement Feature" />
    <bpmn:serviceTask id="TestTask" name="Run Test Matrix" />
    <bpmn:serviceTask id="PrTask" name="Open Pull Request" />
    <bpmn:userTask id="ReviewTask" name="Reviewer Sign-off" />
    <bpmn:endEvent id="EndEvent_1" name="Complete" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ImplementTask" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="ImplementTask" targetRef="TestTask" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="TestTask" targetRef="PrTask" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="PrTask" targetRef="ReviewTask" />
    <bpmn:sequenceFlow id="Flow_5" sourceRef="ReviewTask" targetRef="EndEvent_1" />
  </bpmn:process>
</bpmn:definitions>`,
  },
  {
    id: 'tpl-release',
    name: 'Release Candidate Promotion',
    description: 'Promote release candidate through staging to production.',
    preview: ['Start', 'Package', 'Staging', 'Approval', 'Promote', 'End'],
    xml: `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Defs_Release" targetNamespace="https://autofac.dev/bpmn/extensions/v1">
  <bpmn:process id="ReleasePromotion" name="Release Promotion" isExecutable="true">
    <bpmn:startEvent id="StartEvent_1" name="Release Ready" />
    <bpmn:serviceTask id="PackageTask" name="Package Artifact" />
    <bpmn:serviceTask id="StageDeployTask" name="Deploy to Staging" />
    <bpmn:userTask id="GoNoGoTask" name="Go/No-Go Approval" />
    <bpmn:serviceTask id="ProdDeployTask" name="Promote to Production" />
    <bpmn:endEvent id="EndEvent_1" name="Released" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="PackageTask" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="PackageTask" targetRef="StageDeployTask" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="StageDeployTask" targetRef="GoNoGoTask" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="GoNoGoTask" targetRef="ProdDeployTask" />
    <bpmn:sequenceFlow id="Flow_5" sourceRef="ProdDeployTask" targetRef="EndEvent_1" />
  </bpmn:process>
</bpmn:definitions>`,
  },
];

const NODE_TYPE_LABEL: Record<BpmnNodeType, string> = {
  startEvent: 'Start Event',
  endEvent: 'End Event',
  serviceTask: 'Service Task',
  scriptTask: 'Script Task',
  userTask: 'User Task',
  exclusiveGateway: 'Exclusive Gateway',
  parallelGateway: 'Parallel Gateway',
  subProcess: 'Sub Process',
};

const SUPPORTED_NODE_TYPES: BpmnNodeType[] = [
  'startEvent',
  'serviceTask',
  'scriptTask',
  'userTask',
  'exclusiveGateway',
  'parallelGateway',
  'subProcess',
  'endEvent',
];

function readFileAsText(file: File): Promise<string> {
  if (typeof file.text === 'function') {
    return file.text();
  }

  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(new Error('Unable to read BPMN file.'));
    reader.readAsText(file);
  });
}

function parseBpmnNodes(xml: string): BpmnNodeCard[] {
  if (!xml.trim()) {
    return [];
  }

  const doc = new DOMParser().parseFromString(xml, 'text/xml');
  const parserError = doc.querySelector('parsererror');
  if (parserError) {
    return [];
  }

  const nodes: BpmnNodeCard[] = [];
  const elements = Array.from(doc.getElementsByTagName('*'));

  for (const nodeType of SUPPORTED_NODE_TYPES) {
    for (const element of elements) {
      if (!new RegExp(`(^|:)${nodeType}$`).test(element.tagName)) {
        continue;
      }

      const id = element.getAttribute('id');
      if (!id) {
        continue;
      }

      nodes.push({
        id,
        name: element.getAttribute('name') ?? id,
        type: nodeType,
      });
    }
  }

  return nodes;
}

function normalizeFileName(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '') || 'workflow';
}

function createEmptyMetadata(): NodeMetadataDraft {
  return {
    agent: '',
    action: '',
    environment: '',
    purposeType: '',
    policyTag: '',
  };
}

function getSafeLocalStorage(): Storage | null {
  const storage = globalThis.localStorage;
  if (
    storage &&
    typeof storage.getItem === 'function' &&
    typeof storage.setItem === 'function' &&
    typeof storage.removeItem === 'function'
  ) {
    return storage;
  }

  return null;
}

function createMetadataForNode(node: BpmnNodeCard): NodeMetadataDraft {
  if (node.type === 'userTask') {
    return {
      ...createEmptyMetadata(),
      purposeType: 'production_deployment',
      policyTag: 'approval_required',
    };
  }

  return {
    ...createEmptyMetadata(),
    agent: 'AgentWorker',
    action: 'task.execute',
    environment: 'staging',
    purposeType: 'build_release',
    policyTag: 'task_policy',
  };
}

/**
 * Hook: Auto-save form state to localStorage with a short debounce.
 * Recovers from localStorage on mount
 */
function useAutoSave<T>(
  key: string,
  value: T,
  enabled: boolean = true,
): { recovered: T | null } {
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (!enabled) {
      return;
    }

    // Debounce save
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }

    debounceTimerRef.current = setTimeout(() => {
      try {
        const storage = getSafeLocalStorage();
        storage?.setItem(key, JSON.stringify(value));
      } catch (error) {
        console.error(`Failed to auto-save to localStorage[${key}]:`, error);
      }
    }, AUTO_SAVE_DELAY_MS);

    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
    };
  }, [key, value, enabled]);

  // Recover from localStorage on mount
  const [recovered] = useState<T | null>(() => {
    try {
      const storage = getSafeLocalStorage();
      const item = storage?.getItem(key);
      return item ? JSON.parse(item) : null;
    } catch (error) {
      console.error(`Failed to recover from localStorage[${key}]:`, error);
      return null;
    }
  });

  return { recovered };
}

function buildBpmnDiff(previousXml: string, currentXml: string): DiffLine[] {
  const previousLines = previousXml.split('\n');
  const currentLines = currentXml.split('\n');
  const maxLength = Math.max(previousLines.length, currentLines.length);
  const result: DiffLine[] = [];

  for (let index = 0; index < maxLength; index += 1) {
    const previousLine = previousLines[index] ?? null;
    const currentLine = currentLines[index] ?? null;

    if (previousLine === currentLine && currentLine !== null) {
      result.push({
        lineNumber: index + 1,
        kind: 'unchanged',
        text: currentLine,
      });
      continue;
    }

    if (previousLine !== null) {
      result.push({
        lineNumber: index + 1,
        kind: 'removed',
        text: previousLine,
      });
    }

    if (currentLine !== null) {
      result.push({
        lineNumber: index + 1,
        kind: 'added',
        text: currentLine,
      });
    }
  }

  return result;
}

export function WorkflowDesigner() {
  const [workflows, setWorkflows] = useState<Workflow[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [activeTab, setActiveTab] = useState(inspectorTabs[0]);
  const [bpmnXml, setBpmnXml] = useState('');
  const [validation, setValidation] = useState<WorkflowValidationResult | null>(null);
  const [selectedTemplateId, setSelectedTemplateId] = useState<string | null>(null);
  const [validationLoading, setValidationLoading] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [nodeMetadata, setNodeMetadata] = useState<Record<string, NodeMetadataDraft>>({});
  const [lastPublishedXml, setLastPublishedXml] = useState('');
  const [publishMessage, setPublishMessage] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  // Auto-save: BPMN XML
  const { recovered: recoveredXml } = useAutoSave(
    STORAGE_KEYS.BPMN_XML,
    bpmnXml,
    true,
  );

  // Auto-save: Node metadata
  const { recovered: recoveredMetadata } = useAutoSave(
    STORAGE_KEYS.NODE_METADATA,
    nodeMetadata,
    true,
  );

  // Auto-save: Last published XML
  const { recovered: recoveredLastPublished } = useAutoSave(
    STORAGE_KEYS.LAST_PUBLISHED,
    lastPublishedXml,
    true,
  );

  // Recover from localStorage on mount
  useEffect(() => {
    if (recoveredXml) {
      setBpmnXml(recoveredXml);
    }
    if (recoveredMetadata) {
      setNodeMetadata(recoveredMetadata);
    }
    if (recoveredLastPublished) {
      setLastPublishedXml(recoveredLastPublished);
    }
  }, [recoveredXml, recoveredMetadata, recoveredLastPublished]);

  const loadWorkflows = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await apiClient.getWorkflows();
      setWorkflows(data);
      setSelectedId((current) => current ?? data[0]?.id ?? null);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadWorkflows();
  }, []);

  const filtered = useMemo(() => {
    const query = search.toLowerCase();
    return workflows.filter(
      (workflow) =>
        workflow.name.toLowerCase().includes(query) ||
        workflow.description.toLowerCase().includes(query),
    );
  }, [search, workflows]);

  const selectedWorkflow = workflows.find((workflow) => workflow.id === selectedId) ?? null;
  const bpmnNodes = useMemo(() => parseBpmnNodes(bpmnXml), [bpmnXml]);
  const invalidElementIds = useMemo(() => {
    const ids = new Set<string>();
    for (const issue of validation?.errors ?? []) {
      if (issue.elementId) {
        ids.add(issue.elementId);
      }
    }
    return ids;
  }, [validation]);
  const selectedNode = useMemo(() => {
    if (!selectedNodeId) {
      return null;
    }
    return bpmnNodes.find((node) => node.id === selectedNodeId) ?? null;
  }, [bpmnNodes, selectedNodeId]);
  const selectedNodeMetadata = useMemo(() => {
    if (!selectedNode) {
      return null;
    }
    return nodeMetadata[selectedNode.id] ?? createMetadataForNode(selectedNode);
  }, [nodeMetadata, selectedNode]);
  const metadataIssues = useMemo(() => {
    if (!selectedNode || !selectedNodeMetadata) {
      return [] as string[];
    }

    if (selectedNode.type === 'serviceTask' || selectedNode.type === 'scriptTask') {
      const issues: string[] = [];
      if (!selectedNodeMetadata.agent.trim()) {
        issues.push('Agent is required for service/script tasks.');
      }
      if (!selectedNodeMetadata.action.trim()) {
        issues.push('Action is required for service/script tasks.');
      }
      if (!selectedNodeMetadata.purposeType.trim()) {
        issues.push('Purpose type is required for service/script tasks.');
      }
      if (!selectedNodeMetadata.policyTag.trim()) {
        issues.push('Policy tag is required for service/script tasks.');
      }
      return issues;
    }

    if (selectedNode.type === 'userTask') {
      const issues: string[] = [];
      if (!selectedNodeMetadata.purposeType.trim()) {
        issues.push('Purpose type is required for approval tasks.');
      }
      if (!selectedNodeMetadata.policyTag.trim()) {
        issues.push('Policy tag is required for approval tasks.');
      }
      return issues;
    }

    return [] as string[];
  }, [selectedNode, selectedNodeMetadata]);
  const bpmnDiff = useMemo(() => {
    if (!lastPublishedXml.trim() || !bpmnXml.trim()) {
      return [] as DiffLine[];
    }
    return buildBpmnDiff(lastPublishedXml, bpmnXml);
  }, [bpmnXml, lastPublishedXml]);

  const validateCurrentBpmn = async () => {
    if (!bpmnXml.trim()) {
      setValidationError('Import a BPMN file or paste BPMN XML before validation.');
      return;
    }

    setValidationLoading(true);
    setValidationError(null);
    try {
      const result = await apiClient.validateBpmnWorkflow({
        workflowId: selectedWorkflow?.id,
        bpmnXml,
      });
      setValidation(result);
    } catch (validateError) {
      setValidationError(validateError instanceof Error ? validateError.message : 'Validation failed.');
    } finally {
      setValidationLoading(false);
    }
  };

  const onImportClick = () => {
    fileInputRef.current?.click();
  };

  const onUseTemplate = (templateId: string) => {
    const template = BPMN_TEMPLATE_LIBRARY.find((item) => item.id === templateId);
    if (!template) {
      return;
    }

    setSelectedTemplateId(templateId);
    setBpmnXml(template.xml);
    setSelectedNodeId(null);
    setValidation(null);
    setValidationError(null);
    setPublishMessage(null);
  };

  const onExportClick = () => {
    if (!bpmnXml.trim()) {
      setValidationError('Nothing to export. Import BPMN or choose a template first.');
      return;
    }

    const fileNameBase = normalizeFileName(selectedWorkflow?.name ?? selectedTemplateId ?? 'workflow');
    const blob = new Blob([bpmnXml], { type: 'application/xml;charset=utf-8' });
    const objectUrl = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = `${fileNameBase}.bpmn`;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(objectUrl);
  };

  const onFileSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) {
      return;
    }

    setValidationLoading(true);
    setValidationError(null);
    try {
      const uploaded = await apiClient.uploadBpmnWorkflow(file);
      const xml = await readFileAsText(file);
      setBpmnXml(xml);
      setSelectedNodeId(null);
      setValidation(uploaded.validation);
      if (uploaded.workflowId) {
        setSelectedId((current) => current ?? uploaded.workflowId);
      }
    } catch (uploadError) {
      setValidationError(uploadError instanceof Error ? uploadError.message : 'BPMN import failed.');
    } finally {
      setValidationLoading(false);
      if (fileInputRef.current) {
        fileInputRef.current.value = '';
      }
    }
  };

  const onNodeMetadataChange = (field: keyof NodeMetadataDraft, value: string) => {
    if (!selectedNode) {
      return;
    }

    setNodeMetadata((current) => {
      const previous = current[selectedNode.id] ?? createMetadataForNode(selectedNode);
      return {
        ...current,
        [selectedNode.id]: {
          ...previous,
          [field]: value,
        },
      };
    });
  };

  const onPublishWorkflow = async () => {
    if (!bpmnXml.trim()) {
      setPublishMessage('Import BPMN or choose a template before publishing.');
      return;
    }

    if (metadataIssues.length > 0) {
      setPublishMessage('Resolve metadata validation issues before publishing.');
      return;
    }

    try {
      const publishResult = await apiClient.publishWorkflowDefinition({
        workflowId: selectedWorkflow?.id,
        bpmnXml,
      });
      setLastPublishedXml(bpmnXml);
      setPublishMessage(
        `Published as ${publishResult.version} at ${new Date(publishResult.publishedAt).toLocaleString()}.`,
      );
      
      // Clear draft from localStorage after successful publish
      try {
        const storage = getSafeLocalStorage();
        storage?.removeItem(STORAGE_KEYS.BPMN_XML);
        storage?.removeItem(STORAGE_KEYS.NODE_METADATA);
      } catch {
        // Ignore localStorage errors
      }
      
      await loadWorkflows();
    } catch (publishError) {
      setPublishMessage(
        publishError instanceof Error ? publishError.message : 'Failed to publish workflow.',
      );
    }
  };

  if (loading) {
    return <LoadingState message="Loading workflows" />;
  }

  if (error) {
    return <ErrorState message={error} onRetry={loadWorkflows} />;
  }

  return (
    <section>
      <PageHeader
        title="Workflow Designer"
        description="Design, validate, and publish BPMN workflows."
        actions={
          <div className="inline-actions">
            <button type="button" className="btn btn-secondary" onClick={onImportClick}>
              Import BPMN
            </button>
            <button type="button" className="btn btn-primary">
              New Workflow
            </button>
            <input
              ref={fileInputRef}
              type="file"
              accept=".bpmn,.xml"
              className="sr-only"
              aria-label="Import BPMN file"
              onChange={onFileSelected}
            />
          </div>
        }
      />

      <section className="designer-grid" aria-label="Workflow designer shell">
          <article className="panel designer-list-panel">
            <label htmlFor="workflow-search" className="sr-only">
              Search workflows
            </label>
            <input
              id="workflow-search"
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Search workflows"
            />
            {filtered.length > 0 ? (
              <ul role="list" className="workflow-list">
                {filtered.map((workflow) => (
                  <li key={workflow.id}>
                    <button
                      type="button"
                      className={`workflow-item ${selectedId === workflow.id ? 'workflow-item-active' : ''}`}
                      onClick={() => setSelectedId(workflow.id)}
                    >
                      <strong>{workflow.name}</strong>
                      <span>
                        {workflow.version} - {workflow.status}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            ) : (
              <div className="empty-inline">
                <strong>No workflows available</strong>
                <p>Create your first workflow, import BPMN, or start from a template.</p>
              </div>
            )}
          </article>

          <article className="panel designer-canvas-panel">
            <section className="template-gallery" aria-label="BPMN template gallery">
              <h3>Template Gallery</h3>
              <div className="template-grid" role="list">
                {BPMN_TEMPLATE_LIBRARY.map((template) => (
                  <article key={template.id} className="template-card" role="listitem">
                    <header>
                      <strong>{template.name}</strong>
                      <p>{template.description}</p>
                    </header>
                    <div className="template-preview" aria-hidden="true">
                      {template.preview.map((step) => (
                        <span key={`${template.id}-${step}`}>{step}</span>
                      ))}
                    </div>
                    <button
                      type="button"
                      className="btn btn-secondary"
                      onClick={() => onUseTemplate(template.id)}
                    >
                      Use Template
                    </button>
                  </article>
                ))}
              </div>
            </section>

            <Toolbar>
              <button type="button" className="btn btn-secondary">
                Save
              </button>
              <button type="button" className="btn btn-secondary" onClick={validateCurrentBpmn}>
                Validate
              </button>
              <button type="button" className="btn btn-secondary">
                Simulate
              </button>
              <button type="button" className="btn btn-secondary" onClick={onPublishWorkflow}>
                Publish
              </button>
              <button type="button" className="btn btn-secondary" onClick={onExportClick}>
                Export BPMN
              </button>
            </Toolbar>
            <div className="canvas-placeholder" aria-label="BPMN canvas">
              {bpmnNodes.length > 0 ? (
                <ol className="canvas-node-list" role="list">
                  {bpmnNodes.map((node) => {
                    const hasError = invalidElementIds.has(node.id);
                    return (
                      <li key={node.id}>
                        <button
                          type="button"
                          className={`canvas-node ${hasError ? 'canvas-node-invalid' : ''} ${selectedNodeId === node.id ? 'canvas-node-selected' : ''}`}
                          aria-label={`${node.name} ${NODE_TYPE_LABEL[node.type]}`}
                          onClick={() => setSelectedNodeId(node.id)}
                        >
                          <div>
                            <strong>{node.name}</strong>
                            <span>
                              {NODE_TYPE_LABEL[node.type]} - {node.id}
                            </span>
                          </div>
                          {hasError ? <span className="validation-chip">Validation Error</span> : null}
                        </button>
                      </li>
                    );
                  })}
                </ol>
              ) : (
                <div>
                  <p>Choose a template or import BPMN to render the graph.</p>
                  {selectedWorkflow ? (
                    <p>
                      Selected: {selectedWorkflow.name} {selectedWorkflow.version}
                    </p>
                  ) : null}
                </div>
              )}
            </div>

            <section className="panel validation-panel" aria-label="BPMN validation results">
              <h3>Validation Results</h3>
              {validationLoading ? <p>Validating BPMN...</p> : null}
              {validationError ? <p className="validation-error">{validationError}</p> : null}

              {validation ? (
                <div>
                  <p>
                    Status: <strong>{validation.isValid ? 'Valid' : 'Invalid'}</strong>
                  </p>
                  {validation.processId ? (
                    <p>
                      Process: {validation.processName ?? validation.processId} ({validation.processId})
                    </p>
                  ) : null}
                  {validation.errors.length > 0 ? (
                    <ul className="validation-list">
                      {validation.errors.map((item, index) => (
                        <li key={`${item.elementId ?? item.elementName ?? 'error'}-${index}`}>
                          <strong>{item.elementName ?? 'element'}:</strong> {item.message}
                          {item.elementId ? ` [id: ${item.elementId}]` : ''}
                          {item.lineNumber ? ` at line ${item.lineNumber}` : ''}
                          {item.linePosition ? `, col ${item.linePosition}` : ''}
                        </li>
                      ))}
                    </ul>
                  ) : (
                    <p>No validation errors.</p>
                  )}
                </div>
              ) : (
                <p>Import and validate a BPMN file to see actionable errors.</p>
              )}
            </section>

            {publishMessage ? (
              <section className="panel publish-panel" aria-label="Workflow publish status">
                <h3>Publish Status</h3>
                <p>{publishMessage}</p>
              </section>
            ) : null}

            <section className="panel diff-panel" aria-label="BPMN diff view">
              <h3>Workflow Diff</h3>
              {bpmnDiff.length > 0 ? (
                <ul className="diff-list" role="list">
                  {bpmnDiff.map((entry, index) => (
                    <li key={`${entry.lineNumber}-${entry.kind}-${index}`} className={`diff-line diff-line-${entry.kind}`}>
                      <span className="diff-glyph" aria-hidden="true">
                        {entry.kind === 'added' ? '+' : entry.kind === 'removed' ? '-' : ' '}
                      </span>
                      <code>{entry.text || ' '}</code>
                    </li>
                  ))}
                </ul>
              ) : (
                <p>Publish once to establish a baseline, then edit BPMN to review changes.</p>
              )}
            </section>
          </article>

          <article className="panel designer-inspector-panel">
            <h2>Inspector</h2>
            <div className="tab-row" role="tablist" aria-label="Workflow inspector tabs">
              {inspectorTabs.map((tab) => (
                <button
                  key={tab}
                  type="button"
                  className={`tab ${activeTab === tab ? 'tab-active' : ''}`}
                  role="tab"
                  aria-selected={activeTab === tab}
                  onClick={() => setActiveTab(tab)}
                >
                  {tab}
                </button>
              ))}
            </div>
            <section role="tabpanel" className="tab-panel">
              {selectedWorkflow ? (
                <dl className="definition-list">
                  <div>
                    <dt>Name</dt>
                    <dd>{selectedWorkflow.name}</dd>
                  </div>
                  <div>
                    <dt>Version</dt>
                    <dd>{selectedWorkflow.version}</dd>
                  </div>
                  <div>
                    <dt>Owner</dt>
                    <dd>{selectedWorkflow.owner}</dd>
                  </div>
                  <div>
                    <dt>Validation</dt>
                    <dd>{selectedWorkflow.validationState}</dd>
                  </div>
                </dl>
              ) : (
                <p>Select a workflow to inspect details.</p>
              )}

              <label htmlFor="bpmn-xml">BPMN XML</label>
              <textarea
                id="bpmn-xml"
                rows={8}
                value={bpmnXml}
                placeholder="Paste BPMN XML to validate"
                onChange={(event) => {
                  setBpmnXml(event.target.value);
                  setPublishMessage(null);
                }}
              />

              {selectedNode && selectedNodeMetadata ? (
                <section className="metadata-editor" aria-label="BPMN metadata editor">
                  <h3>Metadata Editor</h3>
                  <p>
                    Selected node: <strong>{selectedNode.name}</strong> ({NODE_TYPE_LABEL[selectedNode.type]})
                  </p>

                  {(selectedNode.type === 'serviceTask' || selectedNode.type === 'scriptTask') && (
                    <>
                      <label htmlFor="meta-agent">Agent</label>
                      <input
                        id="meta-agent"
                        value={selectedNodeMetadata.agent}
                        onChange={(event) => onNodeMetadataChange('agent', event.target.value)}
                        placeholder="e.g. DeployAgent"
                      />

                      <label htmlFor="meta-action">Action</label>
                      <input
                        id="meta-action"
                        value={selectedNodeMetadata.action}
                        onChange={(event) => onNodeMetadataChange('action', event.target.value)}
                        placeholder="e.g. cloud.deploy_artifact"
                      />

                      <label htmlFor="meta-environment">Environment</label>
                      <input
                        id="meta-environment"
                        value={selectedNodeMetadata.environment}
                        onChange={(event) => onNodeMetadataChange('environment', event.target.value)}
                        placeholder="e.g. production"
                      />
                    </>
                  )}

                  <label htmlFor="meta-purpose">Purpose Type</label>
                  <input
                    id="meta-purpose"
                    value={selectedNodeMetadata.purposeType}
                    onChange={(event) => onNodeMetadataChange('purposeType', event.target.value)}
                    placeholder="e.g. production_deployment"
                  />

                  <label htmlFor="meta-policy">Policy Tag</label>
                  <input
                    id="meta-policy"
                    value={selectedNodeMetadata.policyTag}
                    onChange={(event) => onNodeMetadataChange('policyTag', event.target.value)}
                    placeholder="e.g. deploy_approval"
                  />

                  {metadataIssues.length > 0 ? (
                    <ul className="metadata-issues">
                      {metadataIssues.map((issue) => (
                        <li key={issue}>{issue}</li>
                      ))}
                    </ul>
                  ) : (
                    <p className="metadata-valid">Metadata checks passed for this node.</p>
                  )}
                </section>
              ) : (
                <p className="cell-meta">Select a BPMN node to edit extension metadata.</p>
              )}
            </section>
          </article>
      </section>
    </section>
  );
}

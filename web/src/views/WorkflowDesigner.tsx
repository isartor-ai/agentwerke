import { useEffect, useMemo, useRef, useState, type ChangeEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { apiClient } from '../api/client';
import { BpmnModeler, type BpmnModelerHandle } from '../components/BpmnModeler';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import { StatusBadge } from '../components/StatusBadge';
import { Toolbar } from '../components/Toolbar';
import { createEmptyDiagram } from '../bpmn/constants';
import type { Workflow, WorkflowRun, WorkflowValidationResult } from '../types';

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
    preview: ['Start', 'Build', 'Approval', 'Deploy', 'End'],
    xml: `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" xmlns:autofac="https://autofac.dev/bpmn/extensions/v1" id="Defs_Deploy" targetNamespace="https://autofac.dev/bpmn/extensions/v1">
  <bpmn:process id="ProductionDeploy" name="Production Deploy" isExecutable="true">
    <bpmn:startEvent id="StartEvent_1" name="Start">
      <bpmn:outgoing>Flow_1</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:serviceTask id="BuildTask" name="Build Artifact">
      <bpmn:extensionElements>
        <autofac:agentTask agent="BuildAgent" action="ci.build_artifact" environment="build" purposeType="build_release" policyTag="build_gateway" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_1</bpmn:incoming>
      <bpmn:outgoing>Flow_2</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:userTask id="ApprovalTask" name="Release Approval">
      <bpmn:extensionElements>
        <autofac:approvalTask purposeType="production_deployment" policyTag="deploy_approval" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_2</bpmn:incoming>
      <bpmn:outgoing>Flow_3</bpmn:outgoing>
    </bpmn:userTask>
    <bpmn:serviceTask id="DeployTask" name="Deploy to Production">
      <bpmn:extensionElements>
        <autofac:agentTask agent="DeployAgent" action="cloud.deploy_artifact" environment="production" purposeType="production_deployment" policyTag="deploy_gateway" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_3</bpmn:incoming>
      <bpmn:outgoing>Flow_4</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:endEvent id="EndEvent_1" name="Completed">
      <bpmn:incoming>Flow_4</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="BuildTask" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="BuildTask" targetRef="ApprovalTask" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="ApprovalTask" targetRef="DeployTask" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="DeployTask" targetRef="EndEvent_1" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="ProductionDeploy">
      <bpmndi:BPMNShape id="StartEvent_1_di" bpmnElement="StartEvent_1"><dc:Bounds x="152" y="142" width="36" height="36" /></bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="BuildTask_di" bpmnElement="BuildTask"><dc:Bounds x="240" y="120" width="100" height="80" /></bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="ApprovalTask_di" bpmnElement="ApprovalTask"><dc:Bounds x="400" y="120" width="100" height="80" /></bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="DeployTask_di" bpmnElement="DeployTask"><dc:Bounds x="560" y="120" width="100" height="80" /></bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="EndEvent_1_di" bpmnElement="EndEvent_1"><dc:Bounds x="722" y="142" width="36" height="36" /></bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1"><di:waypoint x="188" y="160" /><di:waypoint x="240" y="160" /></bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_2_di" bpmnElement="Flow_2"><di:waypoint x="340" y="160" /><di:waypoint x="400" y="160" /></bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_3_di" bpmnElement="Flow_3"><di:waypoint x="500" y="160" /><di:waypoint x="560" y="160" /></bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_4_di" bpmnElement="Flow_4"><di:waypoint x="660" y="160" /><di:waypoint x="722" y="160" /></bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`,
  },
  {
    id: 'tpl-ci',
    name: 'CI Approval Pipeline',
    description: 'Run tests, then require human approval before merge.',
    preview: ['Start', 'Test', 'Approval', 'Merge', 'End'],
    xml: `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" xmlns:autofac="https://autofac.dev/bpmn/extensions/v1" id="Defs_CI" targetNamespace="https://autofac.dev/bpmn/extensions/v1">
  <bpmn:process id="CiApproval" name="CI Approval" isExecutable="true">
    <bpmn:startEvent id="StartEvent_1" name="Start"><bpmn:outgoing>Flow_1</bpmn:outgoing></bpmn:startEvent>
    <bpmn:serviceTask id="TestTask" name="Run Tests">
      <bpmn:extensionElements>
        <autofac:agentTask agent="TestAgent" action="ci.run_tests" environment="ci" purposeType="quality_gate" policyTag="ci_gateway" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_1</bpmn:incoming><bpmn:outgoing>Flow_2</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:userTask id="ApprovalTask" name="Merge Approval">
      <bpmn:extensionElements>
        <autofac:approvalTask purposeType="merge_request" policyTag="merge_approval" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_2</bpmn:incoming><bpmn:outgoing>Flow_3</bpmn:outgoing>
    </bpmn:userTask>
    <bpmn:serviceTask id="MergeTask" name="Merge PR">
      <bpmn:extensionElements>
        <autofac:agentTask agent="MergeAgent" action="github.merge_pr" environment="production" purposeType="merge_request" policyTag="merge_gateway" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_3</bpmn:incoming><bpmn:outgoing>Flow_4</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:endEvent id="EndEvent_1" name="Done"><bpmn:incoming>Flow_4</bpmn:incoming></bpmn:endEvent>
    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="TestTask" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="TestTask" targetRef="ApprovalTask" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="ApprovalTask" targetRef="MergeTask" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="MergeTask" targetRef="EndEvent_1" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="CiApproval">
      <bpmndi:BPMNShape id="StartEvent_1_di" bpmnElement="StartEvent_1"><dc:Bounds x="152" y="142" width="36" height="36" /></bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="TestTask_di" bpmnElement="TestTask"><dc:Bounds x="240" y="120" width="100" height="80" /></bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="ApprovalTask_di" bpmnElement="ApprovalTask"><dc:Bounds x="400" y="120" width="100" height="80" /></bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="MergeTask_di" bpmnElement="MergeTask"><dc:Bounds x="560" y="120" width="100" height="80" /></bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="EndEvent_1_di" bpmnElement="EndEvent_1"><dc:Bounds x="722" y="142" width="36" height="36" /></bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1"><di:waypoint x="188" y="160" /><di:waypoint x="240" y="160" /></bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_2_di" bpmnElement="Flow_2"><di:waypoint x="340" y="160" /><di:waypoint x="400" y="160" /></bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_3_di" bpmnElement="Flow_3"><di:waypoint x="500" y="160" /><di:waypoint x="560" y="160" /></bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_4_di" bpmnElement="Flow_4"><di:waypoint x="660" y="160" /><di:waypoint x="722" y="160" /></bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`,
  },
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

function normalizeFileName(value: string): string {
  return (
    value
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '') || 'workflow'
  );
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
      result.push({ lineNumber: index + 1, kind: 'unchanged', text: currentLine });
      continue;
    }
    if (previousLine !== null) {
      result.push({ lineNumber: index + 1, kind: 'removed', text: previousLine });
    }
    if (currentLine !== null) {
      result.push({ lineNumber: index + 1, kind: 'added', text: currentLine });
    }
  }

  return result;
}

export function WorkflowDesigner() {
  const navigate = useNavigate();
  const [workflows, setWorkflows] = useState<Workflow[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [currentXml, setCurrentXml] = useState('');
  const [validation, setValidation] = useState<WorkflowValidationResult | null>(null);
  const [selectedTemplateId, setSelectedTemplateId] = useState<string | null>(null);
  const [validationLoading, setValidationLoading] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [lastPublishedXml, setLastPublishedXml] = useState('');
  const [publishMessage, setPublishMessage] = useState<string | null>(null);
  const [publishedWorkflowId, setPublishedWorkflowId] = useState<string | null>(null);
  const [startingRun, setStartingRun] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [workflowDetailLoading, setWorkflowDetailLoading] = useState(false);

  // Design | Monitor mode toggle
  const [designerMode, setDesignerMode] = useState<'design' | 'monitor'>('design');
  const [monitorRuns, setMonitorRuns] = useState<WorkflowRun[]>([]);
  const [monitorLoading, setMonitorLoading] = useState(false);

  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const modelerRef = useRef<BpmnModelerHandle | null>(null);
  const modelerReadyRef = useRef(false);
  const pendingXmlRef = useRef<string | null>(null);
  const nextSelectionValidationRef = useRef<{
    validation: WorkflowValidationResult;
    workflowId: string;
  } | null>(null);

  /** Loads XML into the modeler, queueing if it hasn't mounted yet. */
  const loadXml = (xml: string) => {
    setCurrentXml(xml);
    if (modelerReadyRef.current && modelerRef.current) {
      void modelerRef.current.importXML(xml);
    } else {
      pendingXmlRef.current = xml;
    }
  };

  const handleModelerReady = () => {
    modelerReadyRef.current = true;
    if (pendingXmlRef.current != null) {
      void modelerRef.current?.importXML(pendingXmlRef.current);
      pendingXmlRef.current = null;
    }
  };

  const loadWorkflows = async (preferredWorkflowId?: string | null) => {
    setLoading(true);
    setError(null);
    try {
      const data = await apiClient.getWorkflows();
      setWorkflows(data);
      setSelectedId((current) => preferredWorkflowId ?? current ?? data[0]?.id ?? null);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadWorkflows();
  }, []);

  // Poll runs when Monitor tab is active
  useEffect(() => {
    if (designerMode !== 'monitor') return;
    let cancelled = false;
    const load = async () => {
      setMonitorLoading(true);
      try {
        const all = await apiClient.getRuns();
        if (!cancelled) {
          setMonitorRuns(selectedId ? all.filter((r) => r.workflowId === selectedId) : all);
        }
      } finally {
        if (!cancelled) setMonitorLoading(false);
      }
    };
    void load();
    const timer = setInterval(() => void load(), 10_000);
    return () => { cancelled = true; clearInterval(timer); };
  }, [designerMode, selectedId]);

  useEffect(() => {
    if (!selectedId) {
      return;
    }

    let isCancelled = false;

    const loadWorkflowDetail = async () => {
      setWorkflowDetailLoading(true);
      setValidationError(null);
      try {
        const workflowDetail = await apiClient.getWorkflow(selectedId);
        if (!workflowDetail || isCancelled) {
          return;
        }

        setWorkflows((current) =>
          current.map((workflow) =>
            workflow.id === workflowDetail.id ? workflowDetail : workflow,
          ),
        );

        const pendingValidation = nextSelectionValidationRef.current;
        if (pendingValidation?.workflowId === workflowDetail.id) {
          setValidation(pendingValidation.validation);
          nextSelectionValidationRef.current = null;
        } else {
          setValidation(null);
        }

        setSelectedTemplateId(null);
        setPublishMessage(null);
        const xml = workflowDetail.bpmnXml ?? '';
        setLastPublishedXml(xml);
        loadXml(xml);
      } catch (detailError) {
        if (!isCancelled) {
          setValidationError(
            detailError instanceof Error ? detailError.message : 'Unable to load workflow detail.',
          );
        }
      } finally {
        if (!isCancelled) {
          setWorkflowDetailLoading(false);
        }
      }
    };

    void loadWorkflowDetail();

    return () => {
      isCancelled = true;
    };
  }, [selectedId]);

  const filtered = useMemo(() => {
    const query = search.toLowerCase();
    return workflows.filter(
      (workflow) =>
        workflow.name.toLowerCase().includes(query) ||
        workflow.description.toLowerCase().includes(query),
    );
  }, [search, workflows]);

  const selectedWorkflow = workflows.find((workflow) => workflow.id === selectedId) ?? null;

  const bpmnDiff = useMemo(() => {
    if (!lastPublishedXml.trim() || !currentXml.trim()) {
      return [] as DiffLine[];
    }
    return buildBpmnDiff(lastPublishedXml, currentXml);
  }, [currentXml, lastPublishedXml]);

  const getModelerXml = async (): Promise<string> => {
    const xml = (await modelerRef.current?.getXML()) ?? '';
    return xml.trim() ? xml : currentXml;
  };

  const onNewWorkflow = () => {
    setSelectedId(null);
    setSelectedTemplateId(null);
    setValidation(null);
    setValidationError(null);
    setPublishMessage(null);
    setLastPublishedXml('');
    loadXml(createEmptyDiagram());
  };

  const onUseTemplate = (templateId: string) => {
    const template = BPMN_TEMPLATE_LIBRARY.find((item) => item.id === templateId);
    if (!template) {
      return;
    }
    setSelectedId(null);
    setSelectedTemplateId(templateId);
    setValidation(null);
    setValidationError(null);
    setPublishMessage(null);
    setLastPublishedXml('');
    loadXml(template.xml);
  };

  const validateCurrentBpmn = async () => {
    const xml = await getModelerXml();
    if (!xml.trim()) {
      setValidationError('Add nodes to the canvas or choose a template before validation.');
      return;
    }
    setValidationLoading(true);
    setValidationError(null);
    try {
      const result = await apiClient.validateBpmnWorkflow({
        workflowId: selectedWorkflow?.id,
        bpmnXml: xml,
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

  const onExportClick = async () => {
    const xml = await getModelerXml();
    if (!xml.trim()) {
      setValidationError('Nothing to export. Design a workflow or choose a template first.');
      return;
    }
    const fileNameBase = normalizeFileName(selectedWorkflow?.name ?? selectedTemplateId ?? 'workflow');
    const blob = new Blob([xml], { type: 'application/xml;charset=utf-8' });
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
      const xml = await readFileAsText(file);
      loadXml(xml);
      setLastPublishedXml(xml);
      setSelectedTemplateId(null);
      const uploaded = await apiClient.importWorkflowDefinition({ fileName: file.name, bpmnXml: xml });
      setValidation(uploaded.validation);
      if (uploaded.workflowId) {
        nextSelectionValidationRef.current = {
          validation: uploaded.validation,
          workflowId: uploaded.workflowId,
        };
        setSelectedId(uploaded.workflowId);
        await loadWorkflows(uploaded.workflowId);
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

  const onPublishWorkflow = async () => {
    const xml = await getModelerXml();
    if (!xml.trim()) {
      setPublishMessage('Design a workflow or choose a template before publishing.');
      return;
    }
    try {
      let workflowId = selectedId;
      if (!workflowId) {
        const draftName = selectedTemplateId
          ? `${normalizeFileName(selectedTemplateId)}.bpmn`
          : 'workflow.bpmn';
        const imported = await apiClient.importWorkflowDefinition({ fileName: draftName, bpmnXml: xml });
        workflowId = imported.workflowId;
        setValidation(imported.validation);
        nextSelectionValidationRef.current = { validation: imported.validation, workflowId };
        setSelectedId(workflowId);
        await loadWorkflows(workflowId);
      }

      const publishResult = await apiClient.publishWorkflowDefinition({
        workflowId,
        bpmnXml: xml,
        description: selectedWorkflow?.description,
      });
      setLastPublishedXml(xml);
      setSelectedTemplateId(null);
      setPublishedWorkflowId(workflowId);
      setPublishMessage(
        `Published as ${publishResult.version} at ${new Date(publishResult.publishedAt).toLocaleString()}.`,
      );
      await loadWorkflows(workflowId);
    } catch (publishError) {
      setPublishMessage(
        publishError instanceof Error ? publishError.message : 'Failed to publish workflow.',
      );
    }
  };

  const onStartRun = async () => {
    const workflowId = publishedWorkflowId ?? selectedId;
    if (!workflowId) return;
    setStartingRun(true);
    try {
      const result = await apiClient.startRun(workflowId);
      navigate(`/runs/${result.runId}`);
    } catch {
      navigate('/runs');
    } finally {
      setStartingRun(false);
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
        description="Drag, configure, validate, and publish BPMN workflows with Autofac agent and approval tasks."
        actions={
          <div className="inline-actions">
            <button type="button" className="btn btn-secondary" onClick={onImportClick}>
              Import BPMN
            </button>
            <button type="button" className="btn btn-primary" onClick={onNewWorkflow}>
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

          <section className="template-gallery" aria-label="BPMN template gallery">
            <h3>Templates</h3>
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
        </article>

        <article className="panel designer-canvas-panel">
          {/* Design / Monitor mode toggle */}
          <div className="designer-mode-tabs" role="tablist" aria-label="Designer mode">
            <button
              type="button"
              role="tab"
              aria-selected={designerMode === 'design'}
              className={`tab ${designerMode === 'design' ? 'tab-active' : ''}`}
              onClick={() => setDesignerMode('design')}
            >
              Design
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={designerMode === 'monitor'}
              className={`tab ${designerMode === 'monitor' ? 'tab-active' : ''}`}
              onClick={() => setDesignerMode('monitor')}
            >
              Monitor
            </button>
          </div>

          {designerMode === 'design' ? (
            <>
              <Toolbar>
                <button type="button" className="btn btn-secondary" onClick={validateCurrentBpmn}>
                  Validate
                </button>
                <button type="button" className="btn btn-secondary" onClick={onPublishWorkflow}>
                  Publish
                </button>
                <button type="button" className="btn btn-secondary" onClick={onExportClick}>
                  Export BPMN
                </button>
              </Toolbar>

              <BpmnModeler
                ref={modelerRef}
                onReady={handleModelerReady}
                onChange={(xml) => {
                  setCurrentXml(xml);
                  setPublishMessage(null);
                }}
                onError={(message) => setValidationError(message)}
                validationErrors={validation?.errors ?? []}
              />

              <section className="panel validation-panel" aria-label="BPMN validation results">
                <h3>Validation Results</h3>
                {workflowDetailLoading ? <p>Loading persisted workflow detail...</p> : null}
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
                    {validation.warnings.length > 0 ? (
                      <>
                        <p>
                          Warnings: <strong>{validation.warnings.length}</strong>
                        </p>
                        <ul className="validation-list">
                          {validation.warnings.map((item, index) => (
                            <li key={`${item.elementId ?? item.elementName ?? 'warning'}-${index}`}>
                              <strong>{item.elementName ?? 'element'}:</strong> {item.message}
                              {item.elementId ? ` [id: ${item.elementId}]` : ''}
                              {item.lineNumber ? ` at line ${item.lineNumber}` : ''}
                              {item.linePosition ? `, col ${item.linePosition}` : ''}
                            </li>
                          ))}
                        </ul>
                      </>
                    ) : null}
                  </div>
                ) : (
                  <p>Validate the workflow to see actionable errors on the canvas.</p>
                )}
              </section>

              {publishMessage ? (
                <section className="panel publish-panel" aria-label="Workflow publish status">
                  <h3>Publish Status</h3>
                  <p>{publishMessage}</p>
                  {publishedWorkflowId && (
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={startingRun}
                      onClick={() => void onStartRun()}
                    >
                      {startingRun ? 'Starting…' : 'Start Run'}
                    </button>
                  )}
                </section>
              ) : null}

              {bpmnDiff.length > 0 ? (
                <section className="panel diff-panel" aria-label="BPMN diff view">
                  <h3>Workflow Diff</h3>
                  <ul className="diff-list" role="list">
                    {bpmnDiff.map((entry, index) => (
                      <li
                        key={`${entry.lineNumber}-${entry.kind}-${index}`}
                        className={`diff-line diff-line-${entry.kind}`}
                      >
                        <span className="diff-glyph" aria-hidden="true">
                          {entry.kind === 'added' ? '+' : entry.kind === 'removed' ? '-' : ' '}
                        </span>
                        <code>{entry.text || ' '}</code>
                      </li>
                    ))}
                  </ul>
                </section>
              ) : null}
            </>
          ) : (
            /* Monitor mode — run list for the selected workflow */
            <section className="monitor-panel" aria-label="Run monitor">
              <div className="monitor-header">
                <h3>
                  {selectedWorkflow
                    ? `Runs for ${selectedWorkflow.name}`
                    : 'All Runs'}
                </h3>
                <span className="live-chip">
                  <span aria-hidden="true" />
                  Live · 10s
                </span>
              </div>
              {monitorLoading && monitorRuns.length === 0 ? (
                <p>Loading runs…</p>
              ) : monitorRuns.length === 0 ? (
                <p className="monitor-empty">
                  No runs found for this workflow.{' '}
                  {selectedId && (
                    <button
                      type="button"
                      className="btn btn-primary"
                      onClick={() => void onStartRun()}
                    >
                      Start Run
                    </button>
                  )}
                </p>
              ) : (
                <ul className="monitor-run-list" role="list">
                  {monitorRuns.map((run) => (
                    <li key={run.id}>
                      <Link to={`/runs/${run.id}`} className="monitor-run-item">
                        <span className="monitor-run-id">{run.id}</span>
                        <StatusBadge status={run.status} />
                        <span className="cell-meta">
                          {run.currentStep ?? 'no active step'}
                        </span>
                        <span className="cell-meta">
                          {new Date(run.startedAt).toLocaleString()}
                        </span>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          )}
        </article>
      </section>
    </section>
  );
}

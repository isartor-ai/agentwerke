import { useCallback, useEffect, useMemo, useRef, useState, type ChangeEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { apiClient } from '../api/client';
import { canAdmin, canOperate } from '../auth/permissions';
import { setAgentCatalog } from '../bpmn/agentCatalog';
import { createEmptyDiagram } from '../bpmn/constants';
import { BpmnModeler, type BpmnModelerHandle } from '../components/BpmnModeler';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import { StatusBadge } from '../components/StatusBadge';
import { ToastRegion } from '../components/ToastRegion';
import { Toolbar } from '../components/Toolbar';
import { WorkflowDiffModal } from '../components/WorkflowDiffModal';
import { buildBpmnDiff, formatXml } from '../bpmn/xmlDiff';
import { useToastQueue } from '../components/useToastQueue';
import { buildConfiguredTemplateBpmn } from '../templates/templateBpmn';
import type {
  AuthState,
  RuntimeMode,
  TemplateDetail,
  TemplateFactoryConfiguration,
  TemplateSummary,
  Workflow,
  WorkflowRun,
  WorkflowValidationResult,
} from '../types';

type WorkspaceMode = 'factory' | 'advanced' | 'monitor';
type ConfigurationMapSection = 'requiredInputs' | 'agentAssignments' | 'approvalAssignments' | 'connectors' | 'evidence';

const CONNECTOR_OPTIONS = [
  { id: 'github', label: 'GitHub' },
  { id: 'jira', label: 'Jira' },
  { id: 'ci', label: 'CI' },
  { id: 'slack', label: 'Slack' },
];

const DEFAULT_OWNER = 'platform-eng';

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

function formatToken(value: string): string {
  return value
    .replace(/[_-]+/g, ' ')
    .replace(/\b\w/g, (match) => match.toUpperCase());
}

function createTemplateConfiguration(template: TemplateDetail): TemplateFactoryConfiguration {
  const connectors = Object.fromEntries(
    CONNECTOR_OPTIONS.map((connector) => [
      connector.id,
      template.tags.some((tag) => tag.toLowerCase().includes(connector.id)) ||
        template.trigger.toLowerCase().includes(connector.id),
    ]),
  );

  return {
    name: template.name,
    description: template.description,
    owner: DEFAULT_OWNER,
    requiredInputs: Object.fromEntries(template.requiredInputs.map((input) => [input, ''])),
    agentAssignments: Object.fromEntries(template.agentRoles.map((role) => [role, role])),
    approvalAssignments: Object.fromEntries(template.approvalRoles.map((role) => [role, role])),
    connectors,
    policyLevel: template.policyLevel,
    evidence: Object.fromEntries(template.evidenceExpectations.map((key) => [key, true])),
  };
}

interface WorkflowDesignerProps {
  auth: AuthState;
}

export function WorkflowDesigner({ auth }: WorkflowDesignerProps) {
  const navigate = useNavigate();
  const [workflows, setWorkflows] = useState<Workflow[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [currentXml, setCurrentXml] = useState('');
  const [validation, setValidation] = useState<WorkflowValidationResult | null>(null);
  const [selectedTemplateId, setSelectedTemplateId] = useState<string | null>(null);
  const [selectedTemplate, setSelectedTemplate] = useState<TemplateDetail | null>(null);
  const [templateConfiguration, setTemplateConfiguration] = useState<TemplateFactoryConfiguration | null>(null);
  const [templates, setTemplates] = useState<TemplateSummary[]>([]);
  const [templateLoading, setTemplateLoading] = useState(true);
  const [templateDetailLoading, setTemplateDetailLoading] = useState(false);
  const [templateError, setTemplateError] = useState<string | null>(null);
  const [draftCreating, setDraftCreating] = useState(false);
  const [validationLoading, setValidationLoading] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [lastPublishedXml, setLastPublishedXml] = useState('');
  const [publishMessage, setPublishMessage] = useState<string | null>(null);
  const [publishedWorkflowId, setPublishedWorkflowId] = useState<string | null>(null);
  const [startingRun, setStartingRun] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [workflowDetailLoading, setWorkflowDetailLoading] = useState(false);
  const [workspaceMode, setWorkspaceMode] = useState<WorkspaceMode>('factory');
  const [monitorRuns, setMonitorRuns] = useState<WorkflowRun[]>([]);
  const [monitorLoading, setMonitorLoading] = useState(false);
  const [monitorError, setMonitorError] = useState<string | null>(null);
  const [runtimeMode, setRuntimeMode] = useState<RuntimeMode>({ mode: 'Autofac', camundaEnabled: false });
  const [diffModalOpen, setDiffModalOpen] = useState(false);
  const { toasts, pushToast, dismissToast } = useToastQueue();
  const canAuthorWorkflows = canOperate(auth);
  const canPublishWorkflows = canAdmin(auth);

  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const modelerRef = useRef<BpmnModelerHandle | null>(null);
  const modelerReadyRef = useRef(false);
  const pendingXmlRef = useRef<string | null>(null);
  const monitorRunsRef = useRef<WorkflowRun[]>([]);
  const nextSelectionValidationRef = useRef<{
    validation: WorkflowValidationResult;
    workflowId: string;
  } | null>(null);

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
    const pendingXml = pendingXmlRef.current;
    pendingXmlRef.current = null;
    if (pendingXml != null && pendingXml !== currentXml) {
      void modelerRef.current?.importXML(pendingXml);
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

  const loadTemplates = async () => {
    setTemplateLoading(true);
    setTemplateError(null);
    try {
      const data = await apiClient.getTemplates();
      setTemplates(data);
    } catch (loadError) {
      setTemplateError(loadError instanceof Error ? loadError.message : 'Unable to load templates.');
    } finally {
      setTemplateLoading(false);
    }
  };

  useEffect(() => {
    monitorRunsRef.current = monitorRuns;
  }, [monitorRuns]);

  const loadMonitorRuns = useCallback(async (options: { background?: boolean } = {}) => {
    setMonitorLoading(true);
    setMonitorError(null);
    try {
      const all = await apiClient.getRuns();
      setMonitorRuns(selectedId ? all.filter((run) => run.workflowId === selectedId) : all);
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : 'Unable to load workflow runs.';
      setMonitorError(message);
      if (options.background && monitorRunsRef.current.length > 0) {
        pushToast({ tone: 'error', title: 'Run monitor refresh failed', message });
      }
    } finally {
      setMonitorLoading(false);
    }
  }, [pushToast, selectedId]);

  useEffect(() => {
    void loadWorkflows();
    void loadTemplates();
    void apiClient
      .getAgents()
      .then(setAgentCatalog)
      .catch((catalogError: unknown) => {
        pushToast({
          tone: 'error',
          title: 'Agent catalog unavailable',
          message: catalogError instanceof Error ? catalogError.message : 'Agent metadata could not be loaded.',
        });
      });
    void apiClient
      .getRuntimeMode()
      .then(setRuntimeMode)
      .catch((runtimeError: unknown) => {
        pushToast({
          tone: 'error',
          title: 'Runtime mode unavailable',
          message: runtimeError instanceof Error ? runtimeError.message : 'Using the default Autofac runtime mode.',
        });
      });
  }, [pushToast]);

  useEffect(() => {
    if (workspaceMode !== 'monitor') return;
    void loadMonitorRuns();
    const timer = setInterval(() => void loadMonitorRuns({ background: true }), 10_000);
    return () => clearInterval(timer);
  }, [loadMonitorRuns, workspaceMode]);

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
          setPublishMessage(null);
        }

        setSelectedTemplateId(null);
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

  const changedLineCount = useMemo(() => {
    if (!lastPublishedXml.trim() || !currentXml.trim()) {
      return 0;
    }
    const diff = buildBpmnDiff(formatXml(lastPublishedXml), formatXml(currentXml));
    return diff.filter((line) => line.kind !== 'unchanged').length;
  }, [currentXml, lastPublishedXml]);

  const getModelerXml = async (): Promise<string> => {
    const xml = (await modelerRef.current?.getXML()) ?? '';
    return xml.trim() ? xml : currentXml;
  };

  const updateConfigurationField = (
    field: 'name' | 'description' | 'owner' | 'policyLevel',
    value: string,
  ) => {
    setTemplateConfiguration((current) =>
      current ? { ...current, [field]: value } : current,
    );
  };

  const updateConfigurationMap = (
    section: ConfigurationMapSection,
    key: string,
    value: string | boolean,
  ) => {
    setTemplateConfiguration((current) =>
      current
        ? {
            ...current,
            [section]: {
              ...current[section],
              [key]: value,
            },
          }
        : current,
    );
  };

  const onNewWorkflow = () => {
    setSelectedId(null);
    setSelectedTemplateId(null);
    setSelectedTemplate(null);
    setTemplateConfiguration(null);
    setValidation(null);
    setValidationError(null);
    setPublishMessage(null);
    setLastPublishedXml('');
    setWorkspaceMode('advanced');
    loadXml(createEmptyDiagram());
  };

  const onConfigureTemplate = async (templateId: string) => {
    setWorkspaceMode('factory');
    setSelectedId(null);
    setTemplateDetailLoading(true);
    setTemplateError(null);
    setValidation(null);
    setValidationError(null);
    setPublishMessage(null);
    setPublishedWorkflowId(null);
    try {
      const template = await apiClient.getTemplate(templateId);
      if (!template) {
        throw new Error(`Template '${templateId}' was not found.`);
      }

      setSelectedId(null);
      setSelectedTemplateId(template.id);
      setSelectedTemplate(template);
      setTemplateConfiguration(createTemplateConfiguration(template));
      setLastPublishedXml('');
      loadXml(template.bpmnXml);
    } catch (loadError) {
      setTemplateError(loadError instanceof Error ? loadError.message : 'Unable to load template.');
    } finally {
      setTemplateDetailLoading(false);
    }
  };

  const buildConfiguredXml = (): string | null => {
    if (!selectedTemplate || !templateConfiguration) {
      return null;
    }
    return buildConfiguredTemplateBpmn(selectedTemplate, templateConfiguration);
  };

  const onOpenTemplateAdvanced = () => {
    const xml = buildConfiguredXml();
    if (!xml) {
      return;
    }
    loadXml(xml);
    setWorkspaceMode('advanced');
  };

  const onCreateTemplateDraft = async () => {
    if (!canAuthorWorkflows) {
      pushToast({
        tone: 'error',
        title: 'Operator role required',
        message: 'Creating workflow drafts requires the Operator or Admin role.',
      });
      return;
    }

    const xml = buildConfiguredXml();
    if (!xml || !selectedTemplate || !templateConfiguration) {
      setTemplateError('Select a template before creating a draft.');
      return;
    }

    setDraftCreating(true);
    setTemplateError(null);
    setValidationError(null);
    try {
      const imported = await apiClient.importWorkflowDefinition({
        fileName: `${normalizeFileName(templateConfiguration.name)}.bpmn`,
        bpmnXml: xml,
      });
      setValidation(imported.validation);
      setLastPublishedXml(xml);
      loadXml(xml);
      setSelectedId(imported.workflowId);
      setPublishedWorkflowId(null);
      setPublishMessage(`Draft created from ${selectedTemplate.name}.`);
      nextSelectionValidationRef.current = {
        validation: imported.validation,
        workflowId: imported.workflowId,
      };
      await loadWorkflows(imported.workflowId);
    } catch (createError) {
      setTemplateError(createError instanceof Error ? createError.message : 'Unable to create draft.');
    } finally {
      setDraftCreating(false);
    }
  };

  const validateCurrentBpmn = async () => {
    if (!canAuthorWorkflows) {
      setValidationError('Operator or Admin role required to validate BPMN.');
      return;
    }

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
    if (!canAuthorWorkflows) {
      pushToast({
        tone: 'error',
        title: 'Operator role required',
        message: 'Importing BPMN requires the Operator or Admin role.',
      });
      return;
    }

    setWorkspaceMode('advanced');
    fileInputRef.current?.click();
  };

  const onExportClick = async () => {
    const xml = await getModelerXml();
    if (!xml.trim()) {
      setValidationError('Nothing to export. Design a workflow or choose a template first.');
      return;
    }
    const fileNameBase = normalizeFileName(
      selectedWorkflow?.name ?? templateConfiguration?.name ?? selectedTemplateId ?? 'workflow',
    );
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
    if (!canAuthorWorkflows) {
      if (fileInputRef.current) {
        fileInputRef.current.value = '';
      }
      setValidationError('Operator or Admin role required to import BPMN.');
      return;
    }

    if (!file) {
      return;
    }
    setWorkspaceMode('advanced');
    setValidationLoading(true);
    setValidationError(null);
    try {
      const xml = await readFileAsText(file);
      loadXml(xml);
      setLastPublishedXml(xml);
      setSelectedTemplateId(null);
      setSelectedTemplate(null);
      setTemplateConfiguration(null);
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
    if (!canPublishWorkflows) {
      setPublishMessage('Admin role required to publish workflows.');
      return;
    }

    const xml = await getModelerXml();
    if (!xml.trim()) {
      setPublishMessage('Design a workflow or choose a template before publishing.');
      return;
    }
    try {
      let workflowId = selectedId;
      if (!workflowId) {
        const draftName = templateConfiguration
          ? `${normalizeFileName(templateConfiguration.name)}.bpmn`
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
        description: templateConfiguration?.description ?? selectedWorkflow?.description,
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
    if (!canAuthorWorkflows) {
      pushToast({
        tone: 'error',
        title: 'Operator role required',
        message: 'Starting workflow runs requires the Operator or Admin role.',
      });
      return;
    }

    const workflowId = publishedWorkflowId ?? selectedId;
    if (!workflowId) return;
    setStartingRun(true);
    try {
      const result = await apiClient.startRun(workflowId);
      navigate(`/runs/${result.runId}`);
    } catch (startError) {
      pushToast({
        tone: 'error',
        title: 'Unable to start run',
        message: startError instanceof Error ? startError.message : 'The workflow run could not be created.',
      });
    } finally {
      setStartingRun(false);
    }
  };

  if (loading) {
    return <LoadingState message="Loading workflows" />;
  }

  if (error) {
    return <ErrorState message={error} onRetry={() => void loadWorkflows()} />;
  }

  const validationPanel = (
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

          <section className="validation-section" aria-label="Runtime errors">
            <h4>Runtime Errors</h4>
            <p className="validation-section-hint">
              Issues that block execution on the {runtimeMode.mode} runtime. Fix these before publishing.
            </p>
            {validation.errors.length > 0 ? (
              <ul className="validation-list">
                {validation.errors.map((item, index) => (
                  <li key={`${item.elementId ?? item.elementName ?? 'error'}-${index}`} className="validation-item validation-item-error">
                    <strong>{item.elementName ?? 'element'}:</strong> {item.message}
                    {item.elementId ? ` [id: ${item.elementId}]` : ''}
                    {item.lineNumber ? ` at line ${item.lineNumber}` : ''}
                    {item.linePosition ? `, col ${item.linePosition}` : ''}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="validation-ok">No runtime errors — this workflow is compatible with the {runtimeMode.mode} runtime.</p>
            )}
          </section>

          {validation.warnings.length > 0 ? (
            <section className="validation-section" aria-label="Compatibility warnings">
              <h4>Compatibility Warnings</h4>
              <p className="validation-section-hint">
                {runtimeMode.camundaEnabled
                  ? 'Elements outside the Camunda adapter\'s supported subset. Review adapter settings.'
                  : 'Elements outside the governed default-runtime subset. These are not supported unless a compatible adapter is active.'}
              </p>
              <ul className="validation-list">
                {validation.warnings.map((item, index) => (
                  <li key={`${item.elementId ?? item.elementName ?? 'warning'}-${index}`} className="validation-item validation-item-warning">
                    <strong>{item.elementName ?? 'element'}:</strong> {item.message}
                    {item.elementId ? ` [id: ${item.elementId}]` : ''}
                    {item.lineNumber ? ` at line ${item.lineNumber}` : ''}
                    {item.linePosition ? `, col ${item.linePosition}` : ''}
                  </li>
                ))}
              </ul>
            </section>
          ) : null}
        </div>
      ) : (
        <p>Validate the workflow to see actionable errors on the canvas.</p>
      )}
    </section>
  );

  return (
    <section>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} />
      <PageHeader
        title="SDLC Factory"
        description="Start from a governed SDLC template, assign agents and approvals, then open BPMN only when advanced editing is needed."
        actions={
          <div className="inline-actions">
            <button
              type="button"
              className="btn btn-secondary"
              disabled={!canAuthorWorkflows}
              title={canAuthorWorkflows ? undefined : 'Operator or Admin role required'}
              onClick={onImportClick}
            >
              Import BPMN
            </button>
            <button type="button" className="btn btn-primary" onClick={onNewWorkflow}>
              Blank BPMN
            </button>
            <input
              ref={fileInputRef}
              type="file"
              accept=".bpmn,.xml"
              className="sr-only"
              aria-label="Import BPMN file"
              disabled={!canAuthorWorkflows}
              onChange={onFileSelected}
            />
          </div>
        }
      />

      <section className="designer-grid" aria-label="Workflow authoring shell">
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
                    onClick={() => {
                      setSelectedId(workflow.id);
                      setWorkspaceMode('advanced');
                    }}
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
          <div className="designer-mode-tabs" role="tablist" aria-label="Authoring mode">
            <button
              type="button"
              role="tab"
              aria-selected={workspaceMode === 'factory'}
              className={`tab ${workspaceMode === 'factory' ? 'tab-active' : ''}`}
              onClick={() => setWorkspaceMode('factory')}
            >
              Factory
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={workspaceMode === 'advanced'}
              className={`tab ${workspaceMode === 'advanced' ? 'tab-active' : ''}`}
              onClick={() => setWorkspaceMode('advanced')}
            >
              Advanced BPMN
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={workspaceMode === 'monitor'}
              className={`tab ${workspaceMode === 'monitor' ? 'tab-active' : ''}`}
              onClick={() => setWorkspaceMode('monitor')}
            >
              Monitor
            </button>
          </div>

          {workspaceMode === 'factory' ? (
            <section className="factory-builder" aria-label="Template-first SDLC factory">
              <div className="factory-builder-header">
                <div>
                  <h2>Template Catalog</h2>
                  <p>Choose a governed path, assign operators, and create a workflow draft.</p>
                </div>
                {templateLoading ? <span className="mini-badge neutral">Loading</span> : null}
              </div>

              {templateError ? (
                <ErrorState
                  title="Unable to load templates"
                  message={templateError}
                  onRetry={() => void loadTemplates()}
                  variant="inline"
                />
              ) : null}

              <div className="factory-layout">
                <div className="factory-template-list" role="list" aria-label="SDLC templates">
                  {templates.map((template) => (
                    <article
                      key={template.id}
                      className={`template-card ${selectedTemplateId === template.id ? 'template-card-active' : ''}`}
                      role="listitem"
                    >
                      <header>
                        <strong>{template.name}</strong>
                        <p>{template.description}</p>
                      </header>
                      <div className="template-meta-row">
                        <span className="chip chip-static">{formatToken(template.policyLevel)}</span>
                        <span className="chip chip-static">{formatToken(template.trigger)}</span>
                        <span className="chip chip-static">{template.agentRoles.length} agents</span>
                        <span className="chip chip-static">{template.approvalRoles.length} approvals</span>
                      </div>
                      <button
                        type="button"
                        className="btn btn-secondary"
                        aria-label={`Configure ${template.name}`}
                        onClick={() => void onConfigureTemplate(template.id)}
                      >
                        Configure
                      </button>
                    </article>
                  ))}
                  {!templateLoading && templates.length === 0 ? (
                    <EmptyState
                      title="No templates available"
                      description="The catalog returned no governed SDLC paths."
                      variant="inline"
                      action={
                        <button type="button" className="btn btn-secondary" onClick={() => void loadTemplates()}>
                          Retry templates
                        </button>
                      }
                    />
                  ) : null}
                </div>

                <section className="factory-config-panel" aria-label="Template settings">
                  {templateDetailLoading ? <LoadingState message="Loading template settings" className="compact-loading" /> : null}
                  {selectedTemplate && templateConfiguration ? (
                    <>
                      <div className="factory-config-title">
                        <div>
                          <h3>{selectedTemplate.name}</h3>
                          <p>{selectedTemplate.description}</p>
                        </div>
                        <span className="mini-badge healthy">Default runtime</span>
                      </div>

                      <div className="form-grid">
                        <label>
                          <span>Workflow name</span>
                          <input
                            value={templateConfiguration.name}
                            onChange={(event) => updateConfigurationField('name', event.target.value)}
                          />
                        </label>
                        <label>
                          <span>Owner</span>
                          <input
                            value={templateConfiguration.owner}
                            onChange={(event) => updateConfigurationField('owner', event.target.value)}
                          />
                        </label>
                        <label className="form-span-2">
                          <span>Description</span>
                          <textarea
                            rows={3}
                            value={templateConfiguration.description}
                            onChange={(event) => updateConfigurationField('description', event.target.value)}
                          />
                        </label>
                      </div>

                      {selectedTemplate.requiredInputs.length > 0 ? (
                        <section className="factory-config-section">
                          <h4>Start Inputs</h4>
                          <div className="form-grid">
                            {selectedTemplate.requiredInputs.map((input) => (
                              <label key={input}>
                                <span>{input}</span>
                                <input
                                  value={templateConfiguration.requiredInputs[input] ?? ''}
                                  onChange={(event) => updateConfigurationMap('requiredInputs', input, event.target.value)}
                                />
                              </label>
                            ))}
                          </div>
                        </section>
                      ) : null}

                      <section className="factory-config-section">
                        <h4>Agent Assignments</h4>
                        <div className="form-grid">
                          {selectedTemplate.agentRoles.map((role) => (
                            <label key={role}>
                              <span>{role}</span>
                              <input
                                value={templateConfiguration.agentAssignments[role] ?? ''}
                                onChange={(event) => updateConfigurationMap('agentAssignments', role, event.target.value)}
                              />
                            </label>
                          ))}
                        </div>
                      </section>

                      <section className="factory-config-section">
                        <h4>Approval Owners</h4>
                        <div className="form-grid">
                          {selectedTemplate.approvalRoles.map((role) => (
                            <label key={role}>
                              <span>{role}</span>
                              <input
                                value={templateConfiguration.approvalAssignments[role] ?? ''}
                                onChange={(event) => updateConfigurationMap('approvalAssignments', role, event.target.value)}
                              />
                            </label>
                          ))}
                        </div>
                      </section>

                      <section className="factory-config-section">
                        <h4>Connectors</h4>
                        <div className="checkbox-grid">
                          {CONNECTOR_OPTIONS.map((connector) => (
                            <label key={connector.id} className="checkbox-row">
                              <input
                                type="checkbox"
                                checked={templateConfiguration.connectors[connector.id] ?? false}
                                onChange={(event) => updateConfigurationMap('connectors', connector.id, event.target.checked)}
                              />
                              <span>{connector.label}</span>
                            </label>
                          ))}
                        </div>
                      </section>

                      <section className="factory-config-section">
                        <h4>Policy and Evidence</h4>
                        <div className="form-grid">
                          <label>
                            <span>Policy level</span>
                            <select
                              value={templateConfiguration.policyLevel}
                              onChange={(event) => updateConfigurationField('policyLevel', event.target.value)}
                            >
                              <option value="standard">Standard</option>
                              <option value="elevated">Elevated</option>
                              <option value="critical">Critical</option>
                            </select>
                          </label>
                          <div className="checkbox-grid">
                            {selectedTemplate.evidenceExpectations.map((evidenceKey) => (
                              <label key={evidenceKey} className="checkbox-row">
                                <input
                                  type="checkbox"
                                  checked={templateConfiguration.evidence[evidenceKey] ?? false}
                                  onChange={(event) => updateConfigurationMap('evidence', evidenceKey, event.target.checked)}
                                />
                                <span>{evidenceKey}</span>
                              </label>
                            ))}
                          </div>
                        </div>
                      </section>

                      <Toolbar>
                        <button
                          type="button"
                          className="btn btn-primary"
                          disabled={draftCreating || !canAuthorWorkflows}
                          title={canAuthorWorkflows ? undefined : 'Operator or Admin role required'}
                          onClick={() => void onCreateTemplateDraft()}
                        >
                          {draftCreating ? 'Creating...' : 'Create Draft'}
                        </button>
                        <button type="button" className="btn btn-secondary" onClick={onOpenTemplateAdvanced}>
                          Open Advanced BPMN
                        </button>
                      </Toolbar>

                      {publishMessage ? (
                        <section className="factory-status" aria-label="Factory draft status">
                          <h4>Draft Status</h4>
                          <p>{publishMessage}</p>
                          {validation ? (
                            <p>
                              Validation: <strong>{validation.isValid ? 'Valid' : 'Invalid'}</strong>
                            </p>
                          ) : null}
                          {selectedId ? (
                            <button
                              type="button"
                              className="btn btn-secondary"
                              onClick={() => setWorkspaceMode('advanced')}
                            >
                              Edit in BPMN editor
                            </button>
                          ) : null}
                        </section>
                      ) : null}
                    </>
                  ) : (
                    <EmptyState
                      title="Select a template"
                      description="The configuration panel will show inputs, agents, approval owners, connectors, policy, and evidence."
                      variant="inline"
                    />
                  )}
                </section>
              </div>
            </section>
          ) : null}

          {workspaceMode === 'advanced' ? (
            <>
              <Toolbar>
                <button
                  type="button"
                  className="btn btn-secondary"
                  disabled={!canAuthorWorkflows}
                  title={canAuthorWorkflows ? undefined : 'Operator or Admin role required'}
                  onClick={validateCurrentBpmn}
                >
                  Validate
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  disabled={!canPublishWorkflows}
                  title={canPublishWorkflows ? undefined : 'Admin role required'}
                  onClick={onPublishWorkflow}
                >
                  Publish
                </button>
                <button type="button" className="btn btn-secondary" onClick={onExportClick}>
                  Export BPMN
                </button>
                <span
                  className={`mini-badge ${runtimeMode.camundaEnabled ? 'neutral' : 'healthy'}`}
                  aria-label="Active runtime mode"
                >
                  {runtimeMode.mode} Runtime
                </span>
              </Toolbar>

              <BpmnModeler
                ref={modelerRef}
                initialXml={currentXml}
                camundaMode={runtimeMode.camundaEnabled}
                onReady={handleModelerReady}
                onImportSuccess={() => setValidationError(null)}
                onChange={(xml) => {
                  setCurrentXml(xml);
                  setPublishMessage(null);
                }}
                onError={(message) => setValidationError(message)}
                validationErrors={validation?.errors ?? []}
              />

              {validationPanel}

              {publishMessage ? (
                <section className="panel publish-panel" aria-label="Workflow publish status">
                  <h3>Publish Status</h3>
                  <p>{publishMessage}</p>
                  {publishedWorkflowId ? (
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={startingRun || !canAuthorWorkflows}
                      title={canAuthorWorkflows ? undefined : 'Operator or Admin role required'}
                      onClick={() => void onStartRun()}
                    >
                      {startingRun ? 'Starting...' : 'Start Run'}
                    </button>
                  ) : null}
                </section>
              ) : null}

              {changedLineCount > 0 ? (
                <section className="panel diff-panel" aria-label="Workflow diff summary">
                  <h3>Workflow Diff</h3>
                  <div className="diff-summary-row">
                    <p className="diff-summary-text">
                      Unpublished changes — {changedLineCount} line{changedLineCount === 1 ? '' : 's'} differ from the
                      published version.
                    </p>
                    <button type="button" className="btn btn-secondary" onClick={() => setDiffModalOpen(true)}>
                      View changes
                    </button>
                  </div>
                </section>
              ) : null}
            </>
          ) : null}

          {workspaceMode === 'monitor' ? (
            <section className="monitor-panel" aria-label="Run monitor">
              <div className="monitor-header">
                <h3>
                  {selectedWorkflow
                    ? `Runs for ${selectedWorkflow.name}`
                    : 'All Runs'}
                </h3>
                <div className="monitor-header-actions">
                  {selectedId ? (
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={startingRun || !canAuthorWorkflows}
                      title={canAuthorWorkflows ? undefined : 'Operator or Admin role required'}
                      onClick={() => void onStartRun()}
                    >
                      {startingRun ? 'Starting...' : 'Start Run'}
                    </button>
                  ) : null}
                  <span className="live-chip">
                    <span aria-hidden="true" />
                    Live - 10s
                  </span>
                </div>
              </div>
              {monitorLoading && monitorRuns.length === 0 ? (
                <LoadingState message="Loading workflow runs" className="compact-loading" />
              ) : monitorError && monitorRuns.length === 0 ? (
                <ErrorState
                  title="Unable to load workflow runs"
                  message={monitorError}
                  onRetry={() => void loadMonitorRuns()}
                  retryLabel="Retry monitor"
                  variant="inline"
                />
              ) : monitorRuns.length === 0 ? (
                <EmptyState
                  title={selectedId ? 'No runs found for this workflow' : 'No workflow runs found'}
                  description={
                    selectedId
                      ? 'Start a run to watch this workflow from the monitor.'
                      : 'Select a workflow or start a new run to populate the monitor.'
                  }
                  variant="inline"
                  action={
                    selectedId ? (
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={!canAuthorWorkflows}
                      title={canAuthorWorkflows ? undefined : 'Operator or Admin role required'}
                      onClick={() => void onStartRun()}
                    >
                      Start Run
                    </button>
                    ) : null
                  }
                />
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
          ) : null}
        </article>
      </section>

      {diffModalOpen ? (
        <WorkflowDiffModal
          currentXml={currentXml}
          publishedXml={lastPublishedXml}
          fileName={normalizeFileName(
            selectedWorkflow?.name ?? templateConfiguration?.name ?? selectedTemplateId ?? 'workflow',
          )}
          onClose={() => setDiffModalOpen(false)}
        />
      ) : null}
    </section>
  );
}

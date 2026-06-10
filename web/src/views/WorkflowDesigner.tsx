import { useEffect, useMemo, useState } from 'react';
import { apiClient } from '../api/client';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import { Toolbar } from '../components/Toolbar';
import type { Workflow } from '../types';

const inspectorTabs = ['Details', 'Inputs', 'Policy', 'Agent', 'Retry'];

export function WorkflowDesigner() {
  const [workflows, setWorkflows] = useState<Workflow[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [activeTab, setActiveTab] = useState(inspectorTabs[0]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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
            <button type="button" className="btn btn-secondary">
              Import BPMN
            </button>
            <button type="button" className="btn btn-primary">
              New Workflow
            </button>
          </div>
        }
      />

      {workflows.length === 0 ? (
        <EmptyState
          title="No workflows available"
          description="Create your first workflow or import a BPMN file."
        />
      ) : (
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
                      {workflow.version} · {workflow.status}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </article>

          <article className="panel designer-canvas-panel">
            <Toolbar>
              <button type="button" className="btn btn-secondary">
                Save
              </button>
              <button type="button" className="btn btn-secondary">
                Validate
              </button>
              <button type="button" className="btn btn-secondary">
                Simulate
              </button>
              <button type="button" className="btn btn-secondary">
                Publish
              </button>
              <button type="button" className="btn btn-secondary">
                Export BPMN
              </button>
            </Toolbar>
            <div className="canvas-placeholder">
              <p>BPMN.js canvas integration will be added in a later phase.</p>
              {selectedWorkflow ? (
                <p>
                  Selected: {selectedWorkflow.name} {selectedWorkflow.version}
                </p>
              ) : null}
            </div>
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
            </section>
          </article>
        </section>
      )}
    </section>
  );
}

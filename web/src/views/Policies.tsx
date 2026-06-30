import { useState, useEffect } from 'react';
import { apiClient } from '../api/client';
import { canAdmin } from '../auth/permissions';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import type {
  AuthState,
  PolicyRule,
  PolicyRuleSet,
  PolicySimulationReport,
} from '../types';

interface PoliciesProps {
  auth: AuthState;
}

interface ScenarioForm {
  action: string;
  environment: string;
  purposeType: string;
  policyTag: string;
}

const emptyScenario: ScenarioForm = { action: '', environment: '', purposeType: '', policyTag: '' };

export function Policies({ auth }: PoliciesProps) {
  const admin = canAdmin(auth);

  const [ruleSet, setRuleSet] = useState<PolicyRuleSet | null>(null);
  const [loading, setLoading] = useState(admin);
  const [error, setError] = useState<string | null>(null);
  const [busyRuleId, setBusyRuleId] = useState<string | null>(null);

  const [scenario, setScenario] = useState<ScenarioForm>(emptyScenario);
  const [proposedRuleId, setProposedRuleId] = useState('');
  const [report, setReport] = useState<PolicySimulationReport | null>(null);
  const [simulating, setSimulating] = useState(false);
  const [simError, setSimError] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      setRuleSet(await apiClient.getPolicies());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load policies.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (admin) {
      void load();
    }
  }, [admin]);

  const togglePublish = async (rule: PolicyRule) => {
    setBusyRuleId(rule.id);
    setError(null);
    try {
      if (rule.enabled) {
        await apiClient.unpublishPolicy(rule.id);
      } else {
        await apiClient.publishPolicy(rule.id);
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update policy.');
    } finally {
      setBusyRuleId(null);
    }
  };

  const runSimulation = async () => {
    if (!ruleSet || !scenario.action.trim()) {
      return;
    }
    setSimulating(true);
    setSimError(null);
    setReport(null);
    try {
      // Proposed change = flip the selected rule's enabled flag, previewing a
      // draft↔publish before committing it.
      const proposedRules: PolicyRuleSet | null = proposedRuleId
        ? {
            ...ruleSet,
            rules: ruleSet.rules.map((rule) =>
              rule.id === proposedRuleId ? { ...rule, enabled: !rule.enabled } : rule,
            ),
          }
        : null;

      const result = await apiClient.simulatePolicies({
        proposedRules,
        scenarios: [
          {
            name: scenario.action.trim() || 'scenario',
            action: scenario.action.trim(),
            environment: scenario.environment.trim() || undefined,
            purposeType: scenario.purposeType.trim() || undefined,
            policyTag: scenario.policyTag.trim() || undefined,
          },
        ],
      });
      setReport(result);
    } catch (err) {
      setSimError(err instanceof Error ? err.message : 'Simulation failed.');
    } finally {
      setSimulating(false);
    }
  };

  if (!admin) {
    return (
      <section>
        <PageHeader title="Policies" description="Policy authoring and simulation workflow." />
        <EmptyState
          title="Admin access required"
          description="You need the Admin role to view and manage policies."
        />
      </section>
    );
  }

  if (loading) {
    return (
      <section>
        <PageHeader title="Policies" />
        <LoadingState message="Loading policies..." />
      </section>
    );
  }

  if (error && !ruleSet) {
    return (
      <section>
        <PageHeader title="Policies" />
        <ErrorState message={error} onRetry={() => void load()} />
      </section>
    );
  }

  const rules = ruleSet?.rules ?? [];

  return (
    <section>
      <PageHeader
        title="Policies"
        description="Draft, simulate, and publish policy rules."
        actions={
          <button type="button" className="btn btn-secondary" onClick={() => void load()}>
            Refresh
          </button>
        }
      />

      {error ? <p className="validation-error">{error}</p> : null}

      <article className="panel">
        <h2>Rules</h2>
        {rules.length === 0 ? (
          <EmptyState title="No policy rules" description="No rules are defined yet." variant="inline" />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Decision</th>
                  <th>Risk</th>
                  <th>Status</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {rules.map((rule) => (
                  <tr key={rule.id}>
                    <td>
                      <strong>{rule.name}</strong>
                      <br />
                      <span className="muted">{rule.id}</span>
                    </td>
                    <td>{rule.decisionKind}</td>
                    <td>{rule.riskLevel}</td>
                    <td>
                      <span className={rule.enabled ? 'badge badge-success' : 'badge'}>
                        {rule.enabled ? 'Active' : 'Draft'}
                      </span>
                    </td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-secondary"
                        disabled={busyRuleId === rule.id}
                        onClick={() => void togglePublish(rule)}
                      >
                        {rule.enabled ? 'Unpublish' : 'Publish'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </article>

      <article className="panel">
        <h2>Simulate impact</h2>
        <p className="muted">
          Preview how a scenario is decided under the current rules versus a proposed change, before
          activating it.
        </p>
        <div className="form-grid">
          <label>
            Action
            <input
              value={scenario.action}
              onChange={(event) => setScenario((prev) => ({ ...prev, action: event.target.value }))}
              placeholder="e.g. github.create_pull_request"
            />
          </label>
          <label>
            Environment
            <input
              value={scenario.environment}
              onChange={(event) => setScenario((prev) => ({ ...prev, environment: event.target.value }))}
              placeholder="e.g. production"
            />
          </label>
          <label>
            Purpose
            <input
              value={scenario.purposeType}
              onChange={(event) => setScenario((prev) => ({ ...prev, purposeType: event.target.value }))}
            />
          </label>
          <label>
            Policy tag
            <input
              value={scenario.policyTag}
              onChange={(event) => setScenario((prev) => ({ ...prev, policyTag: event.target.value }))}
            />
          </label>
          <label>
            Proposed change
            <select value={proposedRuleId} onChange={(event) => setProposedRuleId(event.target.value)}>
              <option value="">(none — evaluate current rules)</option>
              {rules.map((rule) => (
                <option key={rule.id} value={rule.id}>
                  {rule.enabled ? 'Unpublish' : 'Publish'} {rule.name}
                </option>
              ))}
            </select>
          </label>
        </div>
        <button
          type="button"
          className="btn btn-primary"
          disabled={simulating || !scenario.action.trim()}
          onClick={() => void runSimulation()}
        >
          {simulating ? 'Simulating...' : 'Run simulation'}
        </button>
        {simError ? <p className="validation-error">{simError}</p> : null}
        {report ? (
          <div className="simulation-result">
            <p>
              <strong>{report.changedCount}</strong> of {report.scenarioCount} scenario(s) would
              change.
            </p>
            {report.outcomes.map((outcome) => (
              <div key={outcome.scenarioName} className="panel narrow-panel">
                <p>
                  <strong>{outcome.scenarioName}</strong>{' '}
                  <span className={outcome.changed ? 'badge badge-warning' : 'badge'}>
                    {outcome.changed ? 'changed' : 'unchanged'}
                  </span>
                </p>
                <p>
                  Current: {outcome.current.kind} (risk {outcome.current.riskLevel}) → Proposed:{' '}
                  {outcome.proposed.kind} (risk {outcome.proposed.riskLevel})
                </p>
                {outcome.changes.length > 0 ? (
                  <ul>
                    {outcome.changes.map((change, index) => (
                      <li key={index}>{change}</li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ))}
          </div>
        ) : null}
      </article>
    </section>
  );
}

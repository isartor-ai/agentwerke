import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { apiClient } from '../api/client';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import type { ConnectorStatus, SettingsTestResponse } from '../types';

interface WebhookEndpoint {
  name: string;
  path: string;
  hint: string;
}

const WEBHOOKS: WebhookEndpoint[] = [
  { name: 'GitHub', path: '/webhooks/github', hint: "Event 'issues'; workflows must carry the 'github-trigger' tag." },
  { name: 'Jira', path: '/webhooks/jira', hint: "Workflows must carry the 'jira-trigger' tag." },
  { name: 'Slack interactivity', path: '/webhooks/slack/interactions', hint: 'Approve/Reject from chat; requires the Slack signing secret.' },
];

export function Integrations() {
  const [connectors, setConnectors] = useState<ConnectorStatus[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [testing, setTesting] = useState<string | null>(null);
  const [testResults, setTestResults] = useState<Record<string, SettingsTestResponse>>({});
  const [copied, setCopied] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      setConnectors(await apiClient.getConnectors());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load connectors.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const test = async (connectorId: string) => {
    setTesting(connectorId);
    try {
      const result = await apiClient.testSettingsTarget(connectorId);
      setTestResults((prev) => ({ ...prev, [connectorId]: result }));
    } catch (err) {
      setTestResults((prev) => ({
        ...prev,
        [connectorId]: {
          target: connectorId,
          succeeded: false,
          messages: [err instanceof Error ? err.message : 'Test failed.'],
          testedAt: '',
          auditId: '',
        },
      }));
    } finally {
      setTesting(null);
    }
  };

  const copy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(text);
      setTimeout(() => setCopied(null), 1500);
    } catch {
      // Clipboard unavailable (e.g. non-secure context) — ignore.
    }
  };

  if (loading) {
    return (
      <section>
        <PageHeader title="Integrations" />
        <LoadingState message="Loading connectors..." />
      </section>
    );
  }

  if (error) {
    return (
      <section>
        <PageHeader title="Integrations" />
        <ErrorState message={error} onRetry={() => void load()} />
      </section>
    );
  }

  const list = connectors ?? [];

  return (
    <section>
      <PageHeader
        title="Integrations"
        description="External connectors and inbound webhook setup."
        actions={
          <button type="button" className="btn btn-secondary" onClick={() => void load()}>
            Refresh
          </button>
        }
      />

      <article className="panel">
        <h2>Connectors</h2>
        <p className="muted">
          Edit credentials in <Link to="/settings">Settings → Integrations</Link>.
        </p>
        {list.length === 0 ? (
          <EmptyState title="No connectors" description="No connectors are registered." variant="inline" />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Connector</th>
                  <th>Status</th>
                  <th>Operations</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {list.map((connector) => {
                  const result = testResults[connector.connectorId];
                  return (
                    <tr key={connector.connectorId}>
                      <td>
                        <strong>{connector.displayName}</strong>
                        <br />
                        <span className="muted">{connector.connectorId}</span>
                      </td>
                      <td>
                        <span className={connector.enabled ? 'badge badge-success' : 'badge'}>
                          {connector.enabled ? 'Enabled' : 'Disabled'}
                        </span>
                      </td>
                      <td>
                        <span className="muted">{connector.supportedOperations.join(', ')}</span>
                      </td>
                      <td>
                        <button
                          type="button"
                          className="btn btn-secondary"
                          disabled={testing === connector.connectorId}
                          onClick={() => void test(connector.connectorId)}
                        >
                          {testing === connector.connectorId ? 'Testing...' : 'Test'}
                        </button>
                        {result ? (
                          <span className={result.succeeded ? 'badge badge-success' : 'validation-error'}>
                            {result.succeeded ? 'OK' : result.messages[0] ?? 'Failed'}
                          </span>
                        ) : null}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </article>

      <article className="panel">
        <h2>Inbound webhooks</h2>
        <p className="muted">Register these URLs in the external system so it can trigger Autofac.</p>
        <ul className="webhook-list">
          {WEBHOOKS.map((webhook) => {
            const url = apiClient.webhookUrl(webhook.path);
            return (
              <li key={webhook.path}>
                <div>
                  <strong>{webhook.name}</strong> <code>{url}</code>
                  <button type="button" className="btn btn-secondary" onClick={() => void copy(url)}>
                    {copied === url ? 'Copied' : 'Copy'}
                  </button>
                </div>
                <p className="muted">{webhook.hint}</p>
              </li>
            );
          })}
        </ul>
      </article>
    </section>
  );
}

import { useEffect, useMemo, useState } from 'react';
import { apiClient } from '../api/client';
import { canAdmin } from '../auth/permissions';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import type {
  AuthState,
  SettingsCategory,
  SettingsField,
  SettingsSnapshot,
  SettingsTestResponse,
} from '../types';

type DraftValue = string | boolean;

interface SettingsProps {
  auth: AuthState;
}

const testTargetsByCategory: Record<string, { id: string; label: string }[]> = {
  model: [{ id: 'model', label: 'Check Model' }],
  integrations: [
    { id: 'github', label: 'Check GitHub' },
    { id: 'jira', label: 'Check Jira' },
    { id: 'slack', label: 'Check Slack' },
    { id: 'teams', label: 'Check Teams' },
  ],
  runtime: [{ id: 'camunda', label: 'Check Camunda' }],
};

function parseList(value: string): string[] {
  return value
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter((item, index, array) => item.length > 0 && array.indexOf(item) === index);
}

function fieldToDraftValue(field: SettingsField): DraftValue {
  if (field.valueType === 'boolean') {
    return field.value === true;
  }

  if (field.valueType === 'string-array') {
    return Array.isArray(field.value) ? field.value.join('\n') : '';
  }

  if (field.valueType === 'string-map') {
    return JSON.stringify(field.value ?? {}, null, 2);
  }

  return field.value == null ? '' : String(field.value);
}

function draftToPayload(field: SettingsField, value: DraftValue): unknown {
  if (field.valueType === 'boolean') {
    return value === true;
  }

  if (field.valueType === 'integer') {
    return Number.parseInt(String(value), 10);
  }

  if (field.valueType === 'decimal') {
    return Number.parseFloat(String(value));
  }

  if (field.valueType === 'string-array') {
    return parseList(String(value));
  }

  if (field.valueType === 'string-map') {
    return JSON.parse(String(value)) as Record<string, string[]>;
  }

  return String(value).trim();
}

function payloadKey(value: unknown): string {
  return JSON.stringify(value);
}

function isFieldDirty(field: SettingsField, draftValue: DraftValue | undefined): boolean {
  if (draftValue === undefined || field.isSecret || !field.isEditable) {
    return false;
  }

  return payloadKey(draftToPayload(field, draftValue)) !== payloadKey(draftToPayload(field, fieldToDraftValue(field)));
}

function statusClass(configured: boolean): string {
  return configured ? 'status-badge status-completed' : 'status-badge status-needs_config';
}

function sourceLabel(source: string): string {
  return source
    .split(/[-_:]/)
    .filter(Boolean)
    .map((part) => part[0]?.toUpperCase() + part.slice(1))
    .join(' ');
}

function fieldValueSummary(field: SettingsField): string {
  if (field.isSecret) {
    if (!field.secret?.configured) {
      return 'Missing';
    }

    return field.secret.fingerprint ? `Configured (${field.secret.fingerprint})` : 'Configured';
  }

  if (Array.isArray(field.value)) {
    return field.value.length > 0 ? `${field.value.length} item(s)` : 'Empty';
  }

  if (field.value && typeof field.value === 'object') {
    return `${Object.keys(field.value).length} mapping(s)`;
  }

  if (field.value === true) {
    return 'Enabled';
  }

  if (field.value === false) {
    return 'Disabled';
  }

  return field.value == null || String(field.value).length === 0 ? 'Empty' : String(field.value);
}

function initializeDrafts(snapshot: SettingsSnapshot): Record<string, DraftValue> {
  return Object.fromEntries(
    snapshot.categories
      .flatMap((category) => category.fields)
      .filter((field) => !field.isSecret)
      .map((field) => [field.path, fieldToDraftValue(field)]),
  );
}

function allFields(snapshot: SettingsSnapshot | null): SettingsField[] {
  return snapshot?.categories.flatMap((category) => category.fields) ?? [];
}

export function Settings({ auth }: SettingsProps) {
  const canManageSettings = canAdmin(auth);
  const [snapshot, setSnapshot] = useState<SettingsSnapshot | null>(null);
  const [selectedCategoryId, setSelectedCategoryId] = useState<string>('model');
  const [draftValues, setDraftValues] = useState<Record<string, DraftValue>>({});
  const [draftSecrets, setDraftSecrets] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(canManageSettings);
  const [saving, setSaving] = useState(false);
  const [testingTarget, setTestingTarget] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<SettingsTestResponse | null>(null);

  const loadSettings = async () => {
    if (!canManageSettings) {
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const nextSnapshot = await apiClient.getSettings();
      setSnapshot(nextSnapshot);
      setDraftValues(initializeDrafts(nextSnapshot));
      setDraftSecrets({});
      setSelectedCategoryId((current) =>
        nextSnapshot.categories.some((category) => category.id === current)
          ? current
          : nextSnapshot.categories[0]?.id ?? 'model',
      );
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Unable to load settings.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadSettings();
  }, [canManageSettings]);

  const selectedCategory = useMemo<SettingsCategory | undefined>(
    () => snapshot?.categories.find((category) => category.id === selectedCategoryId),
    [snapshot, selectedCategoryId],
  );

  const fields = useMemo(() => allFields(snapshot), [snapshot]);
  const dirtyFields = fields.filter((field) => isFieldDirty(field, draftValues[field.path]));
  const pendingSecrets = Object.entries(draftSecrets)
    .filter(([, value]) => value.trim().length > 0)
    .map(([path]) => path);
  const pendingChanges = dirtyFields.length + pendingSecrets.length;

  const updateDraftValue = (field: SettingsField, value: DraftValue) => {
    if (!canManageSettings || !field.isEditable) {
      return;
    }

    setDraftValues((current) => ({ ...current, [field.path]: value }));
    setSaveMessage(null);
    setSaveError(null);
  };

  const updateDraftSecret = (field: SettingsField, value: string) => {
    if (!canManageSettings || !field.isEditable || !field.secret?.canWrite) {
      return;
    }

    setDraftSecrets((current) => ({ ...current, [field.path]: value }));
    setSaveMessage(null);
    setSaveError(null);
  };

  const handleSave = async () => {
    if (!snapshot || !canManageSettings || pendingChanges === 0) {
      return;
    }

    const values: Record<string, unknown> = {};
    for (const field of dirtyFields) {
      values[field.path] = draftToPayload(field, draftValues[field.path]);
    }

    const secrets = Object.fromEntries(
      Object.entries(draftSecrets).filter(([, value]) => value.trim().length > 0),
    );

    setSaving(true);
    setSaveError(null);
    setSaveMessage(null);
    try {
      const response = await apiClient.updateSettings({
        ...(Object.keys(values).length > 0 ? { values } : {}),
        ...(Object.keys(secrets).length > 0 ? { secrets } : {}),
      });
      setSnapshot(response.snapshot);
      setDraftValues(initializeDrafts(response.snapshot));
      setDraftSecrets({});
      setSaveMessage(
        `${response.changedValues.length} setting(s) saved, ${response.rotatedSecrets.length} secret(s) rotated.${
          response.restartRequired ? ' Restart required.' : ''
        }`,
      );
    } catch (saveSettingsError) {
      setSaveError(saveSettingsError instanceof Error ? saveSettingsError.message : 'Unable to save settings.');
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async (target: string) => {
    setTestingTarget(target);
    setTestResult(null);
    try {
      setTestResult(await apiClient.testSettingsTarget(target));
    } catch (testError) {
      setTestResult({
        target,
        succeeded: false,
        messages: [testError instanceof Error ? testError.message : 'Settings check failed.'],
        testedAt: new Date().toISOString(),
        auditId: '',
      });
    } finally {
      setTestingTarget(null);
    }
  };

  if (!canManageSettings) {
    return (
      <section>
        <PageHeader
          title="Settings"
          description="Centralized runtime configuration, integrations, secrets, and authentication controls."
        />
        <article className="panel narrow-panel">
          <EmptyState
            title="Admin role required"
            description="Settings contains platform credentials and authentication controls, so only Admin users can view or change it."
          />
        </article>
      </section>
    );
  }

  if (loading) {
    return <LoadingState message="Loading settings" />;
  }

  if (error) {
    return <ErrorState message={error} onRetry={() => void loadSettings()} />;
  }

  if (!snapshot || !selectedCategory) {
    return (
      <EmptyState
        title="No settings available"
        description="The settings catalog did not return any configuration sections."
      />
    );
  }

  return (
    <section>
      <PageHeader
        title="Settings"
        description="Centralized runtime configuration, integration credentials, authentication, and platform controls."
        actions={
          <>
            <button type="button" className="btn btn-secondary" onClick={() => void loadSettings()}>
              Refresh
            </button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={saving || pendingChanges === 0}
              onClick={() => void handleSave()}
            >
              {saving ? 'Saving...' : `Save Changes (${pendingChanges})`}
            </button>
          </>
        }
      />

      {saveMessage ? <p className="settings-save-message">{saveMessage}</p> : null}
      {saveError ? <p className="validation-error">{saveError}</p> : null}

      <section className="settings-grid">
        <aside className="panel settings-nav-panel" aria-label="Settings sections">
          <div className="panel-title-row">
            <div>
              <span className="panel-kicker">Control Plane</span>
              <h2>Sections</h2>
            </div>
            <span className="chip chip-static">{snapshot.categories.length}</span>
          </div>

          <div className="settings-category-list" role="list">
            {snapshot.categories.map((category) => {
              const missingSecrets = category.fields.filter((field) => field.isSecret && !field.secret?.configured).length;
              return (
                <button
                  key={category.id}
                  type="button"
                  className={`settings-category-button ${
                    category.id === selectedCategory.id ? 'settings-category-button-active' : ''
                  }`}
                  onClick={() => setSelectedCategoryId(category.id)}
                >
                  <strong>{category.title}</strong>
                  <span>{category.description}</span>
                  <span className="cell-meta">
                    {category.fields.length} field(s)
                    {missingSecrets > 0 ? `, ${missingSecrets} missing secret(s)` : ''}
                  </span>
                </button>
              );
            })}
          </div>
        </aside>

        <article className="panel settings-editor-panel">
          <div className="panel-title-row">
            <div>
              <span className="panel-kicker">{selectedCategory.id}</span>
              <h2>{selectedCategory.title}</h2>
            </div>
            <div className="tag-row">
              <span className="chip chip-static">{selectedCategory.fields.length} fields</span>
              {dirtyFields.some((field) => selectedCategory.fields.some((candidate) => candidate.path === field.path)) ? (
                <span className="chip chip-active">Modified</span>
              ) : null}
            </div>
          </div>

          <p className="settings-section-description">{selectedCategory.description}</p>

          {testTargetsByCategory[selectedCategory.id]?.length ? (
            <div className="settings-test-actions" aria-label="Settings checks">
              {testTargetsByCategory[selectedCategory.id].map((target) => (
                <button
                  key={target.id}
                  type="button"
                  className="btn btn-secondary"
                  disabled={testingTarget !== null}
                  onClick={() => void handleTest(target.id)}
                >
                  {testingTarget === target.id ? 'Checking...' : target.label}
                </button>
              ))}
            </div>
          ) : null}

          {testResult ? (
            <div className="settings-test-result" role="status">
              <span className={statusClass(testResult.succeeded)}>
                {testResult.succeeded ? 'Ready' : 'Needs Config'}
              </span>
              <div>
                <strong>{testResult.target}</strong>
                <ul>
                  {testResult.messages.map((message) => (
                    <li key={message}>{message}</li>
                  ))}
                </ul>
              </div>
            </div>
          ) : null}

          <div className="settings-field-list">
            {selectedCategory.fields.map((field) => (
              <section key={field.path} className="settings-field-row">
                <div className="settings-field-meta">
                  <div className="settings-field-heading">
                    <h3>{field.label}</h3>
                    <span className={field.isSecret ? statusClass(field.secret?.configured === true) : 'chip chip-static'}>
                      {fieldValueSummary(field)}
                    </span>
                  </div>
                  <p>{field.description}</p>
                  <div className="tag-row settings-field-tags">
                    <span className="cell-meta">{field.path}</span>
                    <span className="chip chip-static">{sourceLabel(field.source)}</span>
                    {field.requiresRestart ? <span className="chip chip-static">Restart required</span> : null}
                    {!field.isEditable ? <span className="chip chip-static">Read-only</span> : null}
                  </div>
                </div>

                <div className="settings-field-control">
                  {renderFieldControl({
                    field,
                    value: draftValues[field.path],
                    secretValue: draftSecrets[field.path] ?? '',
                    onChange: updateDraftValue,
                    onSecretChange: updateDraftSecret,
                  })}
                </div>
              </section>
            ))}
          </div>
        </article>
      </section>
    </section>
  );
}

function renderFieldControl({
  field,
  value,
  secretValue,
  onChange,
  onSecretChange,
}: {
  field: SettingsField;
  value: DraftValue | undefined;
  secretValue: string;
  onChange: (field: SettingsField, value: DraftValue) => void;
  onSecretChange: (field: SettingsField, value: string) => void;
}) {
  const disabled = !field.isEditable;

  if (field.isSecret) {
    return (
      <label>
        {field.label} rotation
        <input
          type="password"
          autoComplete="off"
          value={secretValue}
          disabled={disabled || field.secret?.canWrite === false}
          placeholder={field.secret?.canWrite === false ? 'Secret writes disabled' : 'Enter a new value to rotate'}
          onChange={(event) => onSecretChange(field, event.target.value)}
        />
      </label>
    );
  }

  if (field.valueType === 'boolean') {
    return (
      <label className="settings-toggle-label">
        <input
          type="checkbox"
          checked={value === true}
          disabled={disabled}
          onChange={(event) => onChange(field, event.target.checked)}
        />
        Enabled
      </label>
    );
  }

  if (field.valueType === 'enum') {
    return (
      <label>
        {field.label}
        <select
          value={String(value ?? '')}
          disabled={disabled}
          onChange={(event) => onChange(field, event.target.value)}
        >
          {field.options.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
      </label>
    );
  }

  if (field.valueType === 'string-array') {
    return (
      <label>
        {field.label}
        <textarea
          rows={4}
          value={String(value ?? '')}
          disabled={disabled}
          onChange={(event) => onChange(field, event.target.value)}
        />
      </label>
    );
  }

  if (field.valueType === 'string-map') {
    return (
      <label>
        {field.label}
        <textarea
          rows={5}
          value={String(value ?? '')}
          disabled
          onChange={(event) => onChange(field, event.target.value)}
        />
      </label>
    );
  }

  return (
    <label>
      {field.label}
      <input
        type={field.valueType === 'integer' || field.valueType === 'decimal' ? 'number' : 'text'}
        value={String(value ?? '')}
        disabled={disabled}
        onChange={(event) => onChange(field, event.target.value)}
      />
    </label>
  );
}

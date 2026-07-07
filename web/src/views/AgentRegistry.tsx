import { type ChangeEvent, useEffect, useMemo, useRef, useState } from 'react';
import { apiClient } from '../api/client';
import { canAdmin } from '../auth/permissions';
import { AgentIdentityBadge } from '../components/AgentIdentityBadge';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import type { AgentDetail, AgentSkillBinding, AgentSummary, AuthState, SkillSummary } from '../types';

interface AgentFormState {
  agentId: string;
  name: string;
  description: string;
  category: string;
  runner: string;
  model: string;
  dockerImage: string;
  network: string;
  tools: string;
  deniedTools: string;
  supportedActions: string;
  supportedEnvironments: string;
  supportedPolicyTags: string;
  secrets: string;
  sandboxProfiles: string;
  identityColor: string;
  identityIcon: string;
  systemPrompt: string;
  skills: AgentSkillBinding[];
}

function parseList(value: string): string[] {
  return value
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter((item, index, array) => item.length > 0 && array.indexOf(item) === index);
}

function formatList(values: string[]): string {
  return values.join('\n');
}

function toFormState(agent: AgentDetail): AgentFormState {
  return {
    agentId: agent.agentId,
    name: agent.name,
    description: agent.description,
    category: agent.category,
    runner: agent.runner,
    model: agent.model ?? '',
    dockerImage: agent.dockerImage ?? '',
    network: agent.network,
    tools: formatList(agent.tools),
    deniedTools: formatList(agent.deniedTools),
    supportedActions: formatList(agent.supportedActions),
    supportedEnvironments: formatList(agent.supportedEnvironments),
    supportedPolicyTags: formatList(agent.supportedPolicyTags),
    secrets: formatList(agent.secrets),
    sandboxProfiles: formatList(agent.sandboxProfiles),
    identityColor: agent.identityColor ?? '',
    identityIcon: agent.identityIcon ?? '',
    systemPrompt: agent.systemPrompt ?? '',
    skills: agent.skills.map((skill) => ({ ...skill })),
  };
}

interface AgentRegistryProps {
  auth: AuthState;
}

export function AgentRegistry({ auth }: AgentRegistryProps) {
  const [agents, setAgents] = useState<AgentSummary[]>([]);
  const [skills, setSkills] = useState<SkillSummary[]>([]);
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null);
  const [selectedAgent, setSelectedAgent] = useState<AgentDetail | null>(null);
  const [form, setForm] = useState<AgentFormState | null>(null);
  const [searchTerm, setSearchTerm] = useState('');
  const [newSkillId, setNewSkillId] = useState('');
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const canManageAgents = canAdmin(auth);

  const loadCatalog = async (nextSelectedAgentId?: string) => {
    setLoading(true);
    setError(null);
    try {
      const [agentsData, skillsData] = await Promise.all([
        apiClient.getAgents(),
        apiClient.getSkills(),
      ]);
      setAgents(agentsData);
      setSkills(skillsData);
      setSelectedAgentId((current) => {
        if (nextSelectedAgentId && agentsData.some((agent) => agent.agentId === nextSelectedAgentId)) {
          return nextSelectedAgentId;
        }

        if (current && agentsData.some((agent) => agent.agentId === current)) {
          return current;
        }

        return agentsData[0]?.agentId ?? null;
      });
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Unable to load the agent registry.');
    } finally {
      setLoading(false);
    }
  };

  const loadAgent = async (agentId: string) => {
    setDetailLoading(true);
    setDetailError(null);
    setSaveError(null);
    try {
      const detail = await apiClient.getAgent(agentId);
      if (!detail) {
        setSelectedAgent(null);
        setForm(null);
        setDetailError(`Agent ${agentId} was not found.`);
        return;
      }

      setSelectedAgent(detail);
      setForm(toFormState(detail));
    } catch (loadError) {
      setDetailError(loadError instanceof Error ? loadError.message : 'Unable to load agent detail.');
    } finally {
      setDetailLoading(false);
    }
  };

  useEffect(() => {
    void loadCatalog();
  }, []);

  useEffect(() => {
    if (!selectedAgentId) {
      setSelectedAgent(null);
      setForm(null);
      return;
    }

    void loadAgent(selectedAgentId);
  }, [selectedAgentId]);

  const filteredAgents = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();
    if (query.length === 0) {
      return agents;
    }

    return agents.filter((agent) =>
      agent.agentId.toLowerCase().includes(query) ||
      agent.name.toLowerCase().includes(query) ||
      agent.category.toLowerCase().includes(query) ||
      agent.source.toLowerCase().includes(query),
    );
  }, [agents, searchTerm]);

  const selectedSkillIds = useMemo(() => new Set(form?.skills.map((skill) => skill.skillId) ?? []), [form]);

  const updateForm = <K extends keyof AgentFormState>(key: K, value: AgentFormState[K]) => {
    if (!canManageAgents) {
      return;
    }

    setForm((current) => (current ? { ...current, [key]: value } : current));
  };

  const toggleCatalogSkill = (skill: SkillSummary) => {
    if (!canManageAgents) {
      return;
    }

    setForm((current) => {
      if (!current) {
        return current;
      }

      const existing = current.skills.find((item) => item.skillId === skill.skillId);
      if (existing) {
        return {
          ...current,
          skills: current.skills.filter((item) => item.skillId !== skill.skillId),
        };
      }

      return {
        ...current,
        skills: [
          ...current.skills,
          {
            skillId: skill.skillId,
            name: skill.name,
            description: skill.description,
            supportedActions: parseList(current.supportedActions),
            skillManifestId: skill.skillId,
          },
        ],
      };
    });
  };

  const updateSkillBinding = (index: number, updater: (skill: AgentSkillBinding) => AgentSkillBinding) => {
    if (!canManageAgents) {
      return;
    }

    setForm((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        skills: current.skills.map((skill, skillIndex) => (skillIndex === index ? updater(skill) : skill)),
      };
    });
  };

  const removeSkillBinding = (index: number) => {
    if (!canManageAgents) {
      return;
    }

    setForm((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        skills: current.skills.filter((_, skillIndex) => skillIndex !== index),
      };
    });
  };

  const addCustomSkill = () => {
    if (!canManageAgents) {
      return;
    }

    const skillId = newSkillId.trim();
    if (!skillId) {
      return;
    }

    setForm((current) => {
      if (!current || current.skills.some((skill) => skill.skillId === skillId)) {
        return current;
      }

      return {
        ...current,
        skills: [
          ...current.skills,
          {
            skillId,
            name: skillId,
            description: '',
            supportedActions: parseList(current.supportedActions),
          },
        ],
      };
    });
    setNewSkillId('');
  };

  const handleSave = async () => {
    if (!form || !canManageAgents) {
      return;
    }

    setSaving(true);
    setSaveError(null);
    try {
      const saved = await apiClient.updateAgent({
        agentId: form.agentId,
        name: form.name,
        description: form.description,
        category: form.category,
        runner: form.runner,
        model: form.model || undefined,
        dockerImage: form.dockerImage || undefined,
        network: form.network,
        tools: parseList(form.tools),
        deniedTools: parseList(form.deniedTools),
        supportedActions: parseList(form.supportedActions),
        skills: form.skills.map((skill) => ({
          ...skill,
          skillId: skill.skillId.trim(),
          name: skill.name.trim(),
          description: skill.description.trim(),
          supportedActions: skill.supportedActions.map((action) => action.trim()).filter((action) => action.length > 0),
          skillManifestId: skill.skillManifestId?.trim() || undefined,
        })),
        supportedEnvironments: parseList(form.supportedEnvironments),
        supportedPolicyTags: parseList(form.supportedPolicyTags),
        secrets: parseList(form.secrets),
        sandboxProfiles: parseList(form.sandboxProfiles),
        identityColor: form.identityColor || undefined,
        identityIcon: form.identityIcon || undefined,
        systemPrompt: form.systemPrompt || undefined,
      });

      setSelectedAgent(saved);
      setForm(toFormState(saved));
      await loadCatalog(saved.agentId);
    } catch (saveAgentError) {
      setSaveError(saveAgentError instanceof Error ? saveAgentError.message : 'Unable to save agent changes.');
    } finally {
      setSaving(false);
    }
  };

  const handleUpload = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';

    if (!file || !canManageAgents) {
      return;
    }

    setUploading(true);
    setUploadError(null);
    try {
      const uploaded = await apiClient.uploadAgent(file);
      setSelectedAgent(uploaded);
      setForm(toFormState(uploaded));
      await loadCatalog(uploaded.agentId);
    } catch (uploadAgentError) {
      setUploadError(uploadAgentError instanceof Error ? uploadAgentError.message : 'Unable to upload the agent file.');
    } finally {
      setUploading(false);
    }
  };

  if (loading) {
    return <LoadingState message="Loading agent registry" />;
  }

  if (error) {
    return <ErrorState message={error} onRetry={() => void loadCatalog()} />;
  }

  return (
    <section>
      <PageHeader
        title="Agents"
        description="View registry metadata, edit file-backed overlays, upload AGENT.md files, and change skill bindings."
        actions={
          <>
            <input
              ref={fileInputRef}
              type="file"
              accept=".md,text/markdown"
              aria-label="Upload AGENT.md file"
              className="sr-only"
              disabled={!canManageAgents}
              onChange={(event) => void handleUpload(event)}
            />
            <button
              type="button"
              className="btn btn-secondary"
              disabled={uploading || !canManageAgents}
              title={canManageAgents ? undefined : 'Admin role required'}
              onClick={() => fileInputRef.current?.click()}
            >
              {uploading ? 'Uploading…' : 'Upload AGENT.md'}
            </button>
            <button type="button" className="btn btn-secondary" onClick={() => void loadCatalog(selectedAgentId ?? undefined)}>
              Refresh
            </button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={!form || saving || !canManageAgents}
              title={canManageAgents ? undefined : 'Admin role required'}
              onClick={() => void handleSave()}
            >
              {saving ? 'Saving…' : 'Save Agent'}
            </button>
          </>
        }
      />

      {uploadError ? <p className="validation-error">{uploadError}</p> : null}

      <section className="agent-registry-grid">
        <article className="panel agent-registry-list-panel">
          <div className="panel-title-row">
            <div>
              <span className="panel-kicker">Catalog</span>
              <h2>Agent Registry</h2>
            </div>
            <span className="chip chip-static">{agents.length} agents</span>
          </div>

          <label className="agent-search-label" htmlFor="agent-registry-search">
            Search agents
          </label>
          <input
            id="agent-registry-search"
            type="search"
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
            placeholder="Search by id, name, category, or source"
          />

          {filteredAgents.length === 0 ? (
            <EmptyState
              title="No agents match"
              description="Try a different search term or upload a new AGENT.md definition."
              className="agent-empty-state"
            />
          ) : (
            <div className="agent-list" role="list" aria-label="Agent registry entries">
              {filteredAgents.map((agent) => (
                <button
                  key={agent.agentId}
                  type="button"
                  className={`agent-list-item ${selectedAgentId === agent.agentId ? 'agent-list-item-active' : ''}`}
                  onClick={() => setSelectedAgentId(agent.agentId)}
                >
                  <div className="agent-list-head">
                    <div className="agent-list-identity">
                      <AgentIdentityBadge
                        name={agent.name}
                        identity={{ color: agent.identityColor, icon: agent.identityIcon }}
                      />
                    </div>
                    <span className="chip chip-static">{agent.source}</span>
                  </div>
                  <p>{agent.description}</p>
                  <div className="tag-row">
                    <span className="cell-meta">{agent.agentId}</span>
                    <span className="cell-meta">{agent.category}</span>
                    <span className="cell-meta">{agent.skills.length} skill(s)</span>
                  </div>
                </button>
              ))}
            </div>
          )}
        </article>

        <article className="panel agent-registry-editor-panel">
          {detailLoading ? (
            <LoadingState message="Loading agent detail" className="compact-loading" />
          ) : detailError ? (
            <ErrorState message={detailError} onRetry={() => selectedAgentId && void loadAgent(selectedAgentId)} />
          ) : !selectedAgent || !form ? (
            <EmptyState
              title="Select an agent"
              description="Choose an entry from the registry to inspect or edit its configuration."
            />
          ) : (
            <div className="agent-editor">
              <div className="panel-title-row">
                <div>
                  <span className="panel-kicker">Editor</span>
                  <h2>{selectedAgent.name}</h2>
                </div>
                <div className="tag-row">
                  <span className="chip chip-static">{selectedAgent.runner}</span>
                  <span className="chip chip-static">{selectedAgent.source}</span>
                </div>
              </div>
              <div className="agent-editor-identity-preview">
                <AgentIdentityBadge
                  name={form.name || selectedAgent.name}
                  identity={{ color: form.identityColor, icon: form.identityIcon }}
                />
              </div>

              {saveError ? <p className="validation-error">{saveError}</p> : null}
              {!canManageAgents ? <p className="cell-meta">Admin role required to edit agent definitions.</p> : null}

              <fieldset className="form-grid form-fieldset" disabled={!canManageAgents}>
                <label>
                  Agent ID
                  <input
                    value={form.agentId}
                    onChange={(event) => updateForm('agentId', event.target.value)}
                  />
                </label>
                <label>
                  Name
                  <input
                    value={form.name}
                    onChange={(event) => updateForm('name', event.target.value)}
                  />
                </label>
                <label className="form-span-2">
                  Description
                  <textarea
                    value={form.description}
                    rows={3}
                    onChange={(event) => updateForm('description', event.target.value)}
                  />
                </label>
                <label>
                  Category
                  <input
                    value={form.category}
                    onChange={(event) => updateForm('category', event.target.value)}
                  />
                </label>
                <label>
                  Runner
                  <select
                    value={form.runner}
                    onChange={(event) => updateForm('runner', event.target.value)}
                  >
                    <option value="agent-model">agent-model</option>
                    <option value="claude-code">claude-code</option>
                  </select>
                </label>
                <label>
                  Model
                  <input
                    value={form.model}
                    onChange={(event) => updateForm('model', event.target.value)}
                  />
                </label>
                <label>
                  Docker Image
                  <input
                    value={form.dockerImage}
                    onChange={(event) => updateForm('dockerImage', event.target.value)}
                  />
                </label>
                <label>
                  Network
                  <select
                    value={form.network}
                    onChange={(event) => updateForm('network', event.target.value)}
                  >
                    <option value="none">none</option>
                    <option value="bridge">bridge</option>
                  </select>
                </label>
                <label>
                  Identity Color
                  <input
                    value={form.identityColor}
                    placeholder="#378ADD"
                    onChange={(event) => updateForm('identityColor', event.target.value)}
                  />
                </label>
                <label>
                  Identity Icon
                  <input
                    value={form.identityIcon}
                    placeholder="⚙"
                    onChange={(event) => updateForm('identityIcon', event.target.value)}
                  />
                </label>
                <label className="form-span-2">
                  Supported Actions
                  <textarea
                    rows={3}
                    value={form.supportedActions}
                    onChange={(event) => updateForm('supportedActions', event.target.value)}
                  />
                </label>
                <label>
                  Tools
                  <textarea
                    rows={4}
                    value={form.tools}
                    onChange={(event) => updateForm('tools', event.target.value)}
                  />
                </label>
                <label>
                  Denied Tools
                  <textarea
                    rows={4}
                    value={form.deniedTools}
                    onChange={(event) => updateForm('deniedTools', event.target.value)}
                  />
                </label>
                <label>
                  Supported Environments
                  <textarea
                    rows={4}
                    value={form.supportedEnvironments}
                    onChange={(event) => updateForm('supportedEnvironments', event.target.value)}
                  />
                </label>
                <label>
                  Supported Policy Tags
                  <textarea
                    rows={4}
                    value={form.supportedPolicyTags}
                    onChange={(event) => updateForm('supportedPolicyTags', event.target.value)}
                  />
                </label>
                <label className="form-span-2">
                  Sandbox Profiles
                  <textarea
                    rows={3}
                    value={form.sandboxProfiles}
                    onChange={(event) => updateForm('sandboxProfiles', event.target.value)}
                  />
                </label>
                <label className="form-span-2">
                  Secrets
                  <textarea
                    rows={3}
                    value={form.secrets}
                    onChange={(event) => updateForm('secrets', event.target.value)}
                  />
                </label>
                <label className="form-span-2">
                  System Prompt
                  <textarea
                    rows={8}
                    value={form.systemPrompt}
                    onChange={(event) => updateForm('systemPrompt', event.target.value)}
                  />
                </label>
              </fieldset>

              <section className="agent-registry-section">
                <div className="panel-title-row">
                  <div>
                    <span className="panel-kicker">Bindings</span>
                    <h2>Agent Skills</h2>
                  </div>
                </div>

                <div className="agent-skill-add-row">
                  <input
                    type="text"
                    value={newSkillId}
                    placeholder="Add custom skill id"
                    disabled={!canManageAgents}
                    onChange={(event) => setNewSkillId(event.target.value)}
                  />
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={!canManageAgents}
                    title={canManageAgents ? undefined : 'Admin role required'}
                    onClick={addCustomSkill}
                  >
                    Add Custom Skill
                  </button>
                </div>

                {form.skills.length === 0 ? (
                  <EmptyState
                    title="No skill bindings"
                    description="Add a catalog skill or a custom skill id to bind behavior to this agent."
                    className="agent-empty-state"
                  />
                ) : (
                  <div className="agent-skill-bindings">
                    {form.skills.map((skill, index) => (
                      <article key={`${skill.skillId}-${index}`} className="agent-skill-binding">
                        <fieldset className="form-grid form-fieldset" disabled={!canManageAgents}>
                          <label>
                            Skill ID
                            <input
                              value={skill.skillId}
                              onChange={(event) => updateSkillBinding(index, (current) => ({
                                ...current,
                                skillId: event.target.value,
                              }))}
                            />
                          </label>
                          <label>
                            Catalog Skill
                            <select
                              value={skill.skillManifestId ?? skill.skillId}
                              onChange={(event) => {
                                const catalogSkill = skills.find((candidate) => candidate.skillId === event.target.value);
                                updateSkillBinding(index, (current) => ({
                                  ...current,
                                  skillId: catalogSkill?.skillId ?? current.skillId,
                                  name: catalogSkill?.name ?? current.name,
                                  description: catalogSkill?.description ?? current.description,
                                  skillManifestId: catalogSkill?.skillId ?? current.skillManifestId,
                                }));
                              }}
                            >
                              <option value={skill.skillManifestId ?? skill.skillId}>custom / unchanged</option>
                              {skills.map((catalogSkill) => (
                                <option key={catalogSkill.skillId} value={catalogSkill.skillId}>
                                  {catalogSkill.name}
                                </option>
                              ))}
                            </select>
                          </label>
                          <label className="form-span-2">
                            Name
                            <input
                              value={skill.name}
                              onChange={(event) => updateSkillBinding(index, (current) => ({
                                ...current,
                                name: event.target.value,
                              }))}
                            />
                          </label>
                          <label className="form-span-2">
                            Description
                            <textarea
                              rows={2}
                              value={skill.description}
                              onChange={(event) => updateSkillBinding(index, (current) => ({
                                ...current,
                                description: event.target.value,
                              }))}
                            />
                          </label>
                          <label className="form-span-2">
                            Supported Actions
                            <textarea
                              rows={3}
                              value={formatList(skill.supportedActions)}
                              onChange={(event) => updateSkillBinding(index, (current) => ({
                                ...current,
                                supportedActions: parseList(event.target.value),
                              }))}
                            />
                          </label>
                        </fieldset>
                        <div className="action-row">
                          <button
                            type="button"
                            className="btn btn-danger"
                            disabled={!canManageAgents}
                            title={canManageAgents ? undefined : 'Admin role required'}
                            onClick={() => removeSkillBinding(index)}
                          >
                            Remove Skill
                          </button>
                        </div>
                      </article>
                    ))}
                  </div>
                )}

                <div className="agent-skill-catalog">
                  <div className="panel-title-row">
                    <div>
                      <span className="panel-kicker">Catalog</span>
                      <h2>Available Skills</h2>
                    </div>
                    <span className="chip chip-static">{skills.length} loaded</span>
                  </div>

                  {skills.length === 0 ? (
                    <EmptyState
                      title="No skills loaded"
                      description="Configure or add skill files to the catalog to bind them from the agent registry."
                      className="agent-empty-state"
                    />
                  ) : (
                    <div className="agent-skill-catalog-list">
                      {skills.map((skill) => (
                        <article key={skill.skillId} className="agent-skill-catalog-item">
                          <div>
                            <strong>{skill.name}</strong>
                            <p>{skill.description}</p>
                            <div className="tag-row">
                              <span className="cell-meta">{skill.skillId}</span>
                              <span className="cell-meta">{skill.version ?? 'unversioned'}</span>
                            </div>
                          </div>
                          <button
                            type="button"
                            className={`btn ${selectedSkillIds.has(skill.skillId) ? 'btn-danger' : 'btn-secondary'}`}
                            disabled={!canManageAgents}
                            title={canManageAgents ? undefined : 'Admin role required'}
                            onClick={() => toggleCatalogSkill(skill)}
                          >
                            {selectedSkillIds.has(skill.skillId) ? 'Remove' : 'Bind'}
                          </button>
                        </article>
                      ))}
                    </div>
                  )}
                </div>
              </section>

              <section className="agent-registry-section">
                <div className="panel-title-row">
                  <div>
                    <span className="panel-kicker">File output</span>
                    <h2>AGENT.md Preview</h2>
                  </div>
                </div>
                <dl className="definition-list">
                  <div><dt>Effective path</dt><dd>{selectedAgent.effectiveFilePath}</dd></div>
                  <div><dt>Source path</dt><dd>{selectedAgent.sourceFilePath ?? 'generated from registry state'}</dd></div>
                </dl>
                <textarea className="agent-source-preview" readOnly rows={16} value={selectedAgent.rawMarkdown} />
              </section>
            </div>
          )}
        </article>
      </section>
    </section>
  );
}

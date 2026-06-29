import { fireEvent, screen, waitFor } from '@testing-library/react';
import { apiClient } from '../api/client';
import type { AgentDetail, AgentSummary, SkillSummary } from '../types';
import { AgentRegistry } from '../views/AgentRegistry';
import { adminAuthFixture, viewerAuthFixture } from './fixtures';
import { renderWithRoute } from './test-utils';

vi.mock('../api/client', () => ({
  apiClient: {
    getAgents: vi.fn(),
    getAgent: vi.fn(),
    updateAgent: vi.fn(),
    uploadAgent: vi.fn(),
    getSkills: vi.fn(),
  },
}));

const agentsFixture: AgentSummary[] = [
  {
    agentId: 'github-agent',
    name: 'GitHub Agent',
    description: 'Reviews pull requests and prepares repository updates.',
    category: 'engineering',
    runner: 'agent-model',
    model: 'gpt-5',
    dockerImage: 'ghcr.io/isartor-ai/github-agent:latest',
    network: 'bridge',
    tools: ['git', 'gh'],
    deniedTools: ['rm'],
    supportedActions: ['review.pull_request'],
    skills: [],
    supportedEnvironments: ['github'],
    supportedPolicyTags: ['repo.read'],
    secrets: ['GITHUB_TOKEN'],
    source: 'file',
    fingerprint: 'agent-fingerprint',
  },
];

const skillsFixture: SkillSummary[] = [
  {
    skillId: 'skill.github-pr',
    name: 'GitHub PR Review',
    description: 'Loads repository context and prepares pull request feedback.',
    version: '1.0.0',
    invocationRules: ['Use for GitHub pull request work.'],
    requiredFiles: ['README.md'],
    optionalTools: ['git'],
    fingerprint: 'skill-fingerprint',
    filePath: '/Users/let7mu/github/autofac/.github/skills/github-pr/SKILL.md',
  },
];

const agentDetailFixture: AgentDetail = {
  ...agentsFixture[0],
  systemPrompt: 'Review the PR carefully and explain tradeoffs.',
  rawMarkdown: `---
id: github-agent
name: GitHub Agent
skills:
  - skill.github-pr
---`,
  effectiveFilePath: '/Users/let7mu/github/autofac/agents/github-agent/AGENT.md',
  sourceFilePath: '/Users/let7mu/github/autofac/agents/github-agent/AGENT.md',
};

describe('AgentRegistry integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getAgents).mockResolvedValue(agentsFixture);
    vi.mocked(apiClient.getSkills).mockResolvedValue(skillsFixture);
    vi.mocked(apiClient.getAgent).mockImplementation(async (agentId: string) => {
      if (agentId === 'github-agent') {
        return agentDetailFixture;
      }

      return {
        ...agentDetailFixture,
        ...uploadedAgentFixture,
      };
    });
    vi.mocked(apiClient.updateAgent).mockResolvedValue({
      ...agentDetailFixture,
      name: 'GitHub Agent v2',
      skills: [
        {
          skillId: 'skill.github-pr',
          name: 'GitHub PR Review',
          description: 'Loads repository context and prepares pull request feedback.',
          supportedActions: ['review.pull_request'],
          skillManifestId: 'skill.github-pr',
        },
      ],
      rawMarkdown: 'updated',
    });
    vi.mocked(apiClient.uploadAgent).mockResolvedValue(uploadedAgentFixture);
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('loads an agent, binds a catalog skill, and saves the updated registry entry', async () => {
    renderWithRoute(<AgentRegistry auth={adminAuthFixture} />, '/agents');

    expect(await screen.findByText('Agent Registry')).toBeInTheDocument();
    expect(await screen.findByDisplayValue('GitHub Agent')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Name'), {
      target: { value: 'GitHub Agent v2' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Bind' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save Agent' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.updateAgent)).toHaveBeenCalledWith(
        expect.objectContaining({
          agentId: 'github-agent',
          name: 'GitHub Agent v2',
          skills: [
            expect.objectContaining({
              skillId: 'skill.github-pr',
              skillManifestId: 'skill.github-pr',
              supportedActions: ['review.pull_request'],
            }),
          ],
        }),
      );
    });

    expect(await screen.findByDisplayValue('GitHub Agent v2')).toBeInTheDocument();
  });

  it('uploads an AGENT.md file and switches the editor to the uploaded agent', async () => {
    vi.mocked(apiClient.getAgents)
      .mockResolvedValueOnce(agentsFixture)
      .mockResolvedValueOnce([
        ...agentsFixture,
        {
          ...uploadedAgentFixture,
          source: 'file',
        },
      ]);

    renderWithRoute(<AgentRegistry auth={adminAuthFixture} />, '/agents');

    expect(await screen.findByDisplayValue('GitHub Agent')).toBeInTheDocument();

    const upload = screen.getByLabelText('Upload AGENT.md file');
    const file = new File(['---\nid: ops-agent\nname: Ops Agent\n---'], 'AGENT.md', {
      type: 'text/markdown',
    });

    fireEvent.change(upload, { target: { files: [file] } });

    await waitFor(() => {
      expect(vi.mocked(apiClient.uploadAgent)).toHaveBeenCalledWith(file);
    });

    expect(await screen.findByDisplayValue('Ops Agent')).toBeInTheDocument();
  });

  it('keeps agent editing controls read-only for viewers', async () => {
    renderWithRoute(<AgentRegistry auth={viewerAuthFixture} />, '/agents');

    expect(await screen.findByDisplayValue('GitHub Agent')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Bind' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Save Agent' })).toBeDisabled();
    expect(screen.getByText('Admin role required to edit agent definitions.')).toBeInTheDocument();
  });
});

const uploadedAgentFixture: AgentDetail = {
  ...agentsFixture[0],
  agentId: 'ops-agent',
  name: 'Ops Agent',
  description: 'Coordinates operational workflows and rollback checks.',
  category: 'operations',
  rawMarkdown: `---
id: ops-agent
name: Ops Agent
---`,
  effectiveFilePath: '/Users/let7mu/github/autofac/agents/ops-agent/AGENT.md',
  sourceFilePath: '/Users/let7mu/github/autofac/agents/ops-agent/AGENT.md',
};

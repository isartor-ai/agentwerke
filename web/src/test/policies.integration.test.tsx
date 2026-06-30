import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import type { PolicyRuleSet, PolicySimulationReport } from '../types';
import { Policies } from '../views/Policies';
import { adminAuthFixture, viewerAuthFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getPolicies: vi.fn(),
    publishPolicy: vi.fn(),
    unpublishPolicy: vi.fn(),
    simulatePolicies: vi.fn(),
  },
}));

const ruleSetFixture: PolicyRuleSet = {
  version: 'v1',
  updatedAt: new Date().toISOString(),
  rules: [
    {
      id: 'rule-active',
      name: 'Active Rule',
      enabled: true,
      priority: 1,
      decisionKind: 'escalate',
      rationale: '',
      riskScore: 60,
      riskLevel: 'medium',
      riskFactors: [],
      constraints: [],
      predicates: [],
    },
    {
      id: 'rule-draft',
      name: 'Draft Rule',
      enabled: false,
      priority: 2,
      decisionKind: 'reject',
      rationale: '',
      riskScore: 90,
      riskLevel: 'critical',
      riskFactors: [],
      constraints: [],
      predicates: [],
    },
  ],
};

const reportFixture: PolicySimulationReport = {
  scenarioCount: 1,
  changedCount: 1,
  outcomes: [
    {
      scenarioName: 'secret.export',
      changed: true,
      current: {
        kind: 'allow',
        policyId: 'default-allow',
        policyName: '',
        rationale: '',
        riskScore: 18,
        riskLevel: 'low',
        riskFactors: [],
        decidedAt: '',
        constraints: [],
      },
      proposed: {
        kind: 'reject',
        policyId: 'rule-draft',
        policyName: '',
        rationale: '',
        riskScore: 90,
        riskLevel: 'critical',
        riskFactors: [],
        decidedAt: '',
        constraints: [],
      },
      changes: ['decision: allow → reject'],
    },
  ],
};

describe('Policies integration', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.getPolicies).mockResolvedValue(ruleSetFixture);
    vi.mocked(apiClient.publishPolicy).mockResolvedValue(ruleSetFixture.rules[1]);
    vi.mocked(apiClient.unpublishPolicy).mockResolvedValue(ruleSetFixture.rules[0]);
    vi.mocked(apiClient.simulatePolicies).mockResolvedValue(reportFixture);
  });

  it('lists rules with status and publishes a draft rule', async () => {
    render(
      <MemoryRouter>
        <Policies auth={adminAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Active Rule')).toBeInTheDocument();
    expect(screen.getByText('Draft')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Publish' }));

    await waitFor(() => expect(apiClient.publishPolicy).toHaveBeenCalledWith('rule-draft'));
  });

  it('runs a simulation and renders the impact diff', async () => {
    render(
      <MemoryRouter>
        <Policies auth={adminAuthFixture} />
      </MemoryRouter>,
    );

    await screen.findByText('Active Rule');
    fireEvent.change(screen.getByPlaceholderText('e.g. github.create_pull_request'), {
      target: { value: 'secret.export' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Run simulation' }));

    await waitFor(() => expect(apiClient.simulatePolicies).toHaveBeenCalled());
    expect(await screen.findByText('decision: allow → reject')).toBeInTheDocument();
  });

  it('blocks non-admins', async () => {
    render(
      <MemoryRouter>
        <Policies auth={viewerAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Admin access required')).toBeInTheDocument();
    expect(apiClient.getPolicies).not.toHaveBeenCalled();
  });
});

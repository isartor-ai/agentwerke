import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import type { ConnectorStatus } from '../types';
import { Integrations } from '../views/Integrations';

vi.mock('../api/client', () => ({
  apiClient: {
    getConnectors: vi.fn(),
    testSettingsTarget: vi.fn(),
    webhookUrl: (path: string) => `https://api.test${path}`,
  },
}));

const connectorsFixture: ConnectorStatus[] = [
  {
    connectorId: 'github',
    displayName: 'GitHub',
    enabled: true,
    supportedOperations: ['create_branch', 'create_pull_request'],
  },
  {
    connectorId: 'slack',
    displayName: 'Slack',
    enabled: false,
    supportedOperations: ['send_notification'],
  },
];

describe('Integrations', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.getConnectors).mockResolvedValue(connectorsFixture);
    vi.mocked(apiClient.testSettingsTarget).mockResolvedValue({
      target: 'github',
      succeeded: true,
      messages: [],
      testedAt: '',
      auditId: '',
    });
  });

  it('lists connectors with status and webhook URLs', async () => {
    render(
      <MemoryRouter>
        <Integrations />
      </MemoryRouter>,
    );

    expect(await screen.findByText('github')).toBeInTheDocument();
    expect(screen.getByText('Enabled')).toBeInTheDocument();
    expect(screen.getByText('Disabled')).toBeInTheDocument();
    expect(screen.getByText('https://api.test/webhooks/github')).toBeInTheDocument();
  });

  it('tests a connector and shows the result', async () => {
    render(
      <MemoryRouter>
        <Integrations />
      </MemoryRouter>,
    );

    await screen.findByText('github');
    fireEvent.click(screen.getAllByRole('button', { name: 'Test' })[0]);

    await waitFor(() => expect(apiClient.testSettingsTarget).toHaveBeenCalledWith('github'));
    expect(await screen.findByText('OK')).toBeInTheDocument();
  });
});

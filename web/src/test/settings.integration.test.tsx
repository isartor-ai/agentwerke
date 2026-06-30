import { fireEvent, screen, waitFor } from '@testing-library/react';
import { apiClient } from '../api/client';
import type { SettingsSnapshot, SettingsUpdateResponse } from '../types';
import { Settings } from '../views/Settings';
import { adminAuthFixture, viewerAuthFixture } from './fixtures';
import { renderWithRoute } from './test-utils';

vi.mock('../api/client', () => ({
  apiClient: {
    getSettings: vi.fn(),
    updateSettings: vi.fn(),
    testSettingsTarget: vi.fn(),
  },
}));

const settingsFixture: SettingsSnapshot = {
  generatedAt: new Date().toISOString(),
  categories: [
    {
      id: 'model',
      title: 'Model',
      description: 'Language-model provider settings.',
      fields: [
        {
          path: 'Anthropic:Provider',
          label: 'Provider',
          description: 'Language-model backend selection.',
          valueType: 'enum',
          value: 'anthropic',
          isSecret: false,
          isEditable: true,
          requiresRestart: true,
          source: 'configuration',
          options: ['auto', 'anthropic', 'mock'],
          secret: null,
        },
        {
          path: 'Anthropic:ApiKey',
          label: 'API key',
          description: 'Provider API key.',
          valueType: 'secret',
          value: null,
          isSecret: true,
          isEditable: true,
          requiresRestart: true,
          source: 'configuration',
          options: [],
          secret: {
            configured: true,
            source: 'configuration',
            fingerprint: 'abc123def456',
            canWrite: true,
          },
        },
      ],
    },
    {
      id: 'integrations',
      title: 'Integrations',
      description: 'Connector settings.',
      fields: [
        {
          path: 'Integrations:GitHub:Enabled',
          label: 'GitHub enabled',
          description: 'Enable GitHub connector.',
          valueType: 'boolean',
          value: false,
          isSecret: false,
          isEditable: true,
          requiresRestart: true,
          source: 'default',
          options: [],
          secret: null,
        },
      ],
    },
  ],
};

describe('Settings integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getSettings).mockResolvedValue(settingsFixture);
    vi.mocked(apiClient.updateSettings).mockImplementation(async (payload): Promise<SettingsUpdateResponse> => ({
      snapshot: {
        ...settingsFixture,
        categories: settingsFixture.categories.map((category) =>
          category.id === 'model'
            ? {
                ...category,
                fields: category.fields.map((field) =>
                  field.path === 'Anthropic:Provider' ? { ...field, value: 'mock', source: 'settings-overrides' } : field,
                ),
              }
            : category,
        ),
      },
      changedValues: Object.keys(payload.values ?? {}),
      rotatedSecrets: Object.keys(payload.secrets ?? {}),
      restartRequired: true,
      auditId: 'audit-settings',
    }));
    vi.mocked(apiClient.testSettingsTarget).mockResolvedValue({
      target: 'model',
      succeeded: true,
      messages: ['Mock model provider is selected; no external credential is required.'],
      testedAt: new Date().toISOString(),
      auditId: 'audit-test',
    });
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('loads settings, saves changed values, and rotates secrets without displaying the new secret', async () => {
    renderWithRoute(<Settings auth={adminAuthFixture} />, '/settings');

    expect(await screen.findByText('Control Plane')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Provider'), {
      target: { value: 'mock' },
    });
    fireEvent.change(screen.getByLabelText('API key rotation'), {
      target: { value: 'new-api-key' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes (2)' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.updateSettings)).toHaveBeenCalledWith({
        values: {
          'Anthropic:Provider': 'mock',
        },
        secrets: {
          'Anthropic:ApiKey': 'new-api-key',
        },
      });
    });

    expect(await screen.findByText('1 setting(s) saved, 1 secret(s) rotated. Restart required.')).toBeInTheDocument();
    expect(screen.queryByDisplayValue('new-api-key')).not.toBeInTheDocument();
  });

  it('runs a settings readiness check for the active section', async () => {
    renderWithRoute(<Settings auth={adminAuthFixture} />, '/settings');

    expect(await screen.findByRole('button', { name: 'Check Model' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Check Model' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.testSettingsTarget)).toHaveBeenCalledWith('model');
    });
    expect(await screen.findByText('Mock model provider is selected; no external credential is required.')).toBeInTheDocument();
  });

  it('does not fetch or expose settings for non-admin users', () => {
    renderWithRoute(<Settings auth={viewerAuthFixture} />, '/settings');

    expect(screen.getByText('Admin role required')).toBeInTheDocument();
    expect(vi.mocked(apiClient.getSettings)).not.toHaveBeenCalled();
  });
});

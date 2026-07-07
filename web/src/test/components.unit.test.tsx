import { fireEvent, render, screen } from '@testing-library/react';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { DataTable, type DataTableColumn } from '../components/DataTable';
import { FilterBar } from '../components/FilterBar';
import { RiskBadge } from '../components/RiskBadge';
import { StatusBadge } from '../components/StatusBadge';
import { StepTimeline } from '../components/StepTimeline';

describe('UI component unit tests', () => {
  it('renders status and risk badges with labels', () => {
    render(
      <>
        <StatusBadge status="awaiting_approval" />
        <RiskBadge level="critical" score={91} />
      </>,
    );

    expect(screen.getByText('Awaiting Approval')).toBeInTheDocument();
    expect(screen.getByText('Critical 91')).toBeInTheDocument();
  });

  it('calls filter callbacks when filter chips are selected', () => {
    const onStatusChange = vi.fn();
    const onRiskChange = vi.fn();

    render(
      <FilterBar
        status="all"
        risk="all"
        onStatusChange={onStatusChange}
        onRiskChange={onRiskChange}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Running' }));
    fireEvent.click(screen.getByRole('button', { name: 'High' }));

    expect(onStatusChange).toHaveBeenCalledWith('running');
    expect(onRiskChange).toHaveBeenCalledWith('high');
  });

  it('supports mouse and keyboard row activation in DataTable', () => {
    const onRowClick = vi.fn();
    const columns: DataTableColumn<{ id: string; name: string }>[] = [
      { key: 'name', label: 'Name', render: (row) => row.name },
    ];

    render(
      <DataTable
        caption="sample"
        columns={columns}
        rows={[{ id: '1', name: 'Row 1' }]}
        rowKey={(row) => row.id}
        onRowClick={onRowClick}
        rowAriaLabel={(row) => `Open ${row.name}`}
      />,
    );

    const row = screen.getByText('Row 1').closest('tr');
    expect(row).not.toBeNull();
    expect(row).toHaveAttribute('aria-label', 'Open Row 1');

    fireEvent.click(row!);
    fireEvent.keyDown(row!, { key: 'Enter' });

    expect(onRowClick).toHaveBeenCalledTimes(2);
  });

  it('shows dialog only when open and triggers callbacks', () => {
    const onConfirm = vi.fn();
    const onCancel = vi.fn();

    const { rerender } = render(
      <ConfirmDialog
        title="Cancel run"
        body="Confirm"
        open={false}
        onConfirm={onConfirm}
        onCancel={onCancel}
      />,
    );

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    rerender(
      <ConfirmDialog
        title="Cancel run"
        body="Confirm"
        open
        onConfirm={onConfirm}
        onCancel={onCancel}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('toggles timeline item details', () => {
    const onToggleStep = vi.fn();
    render(
      <StepTimeline
        steps={[
          { id: 'step-1', name: 'Trigger', type: 'start', status: 'completed' },
          {
            id: 'step-2',
            name: 'Approval',
            type: 'user_task',
            status: 'awaiting_approval',
            policyDecision: {
              kind: 'escalate',
              policyId: 'p1',
              policyName: 'Policy',
              rationale: 'Needs approval',
              riskScore: 80,
              riskLevel: 'high',
              riskFactors: ['prod'],
              decidedAt: new Date().toISOString(),
            },
          },
        ]}
        expandedStepId={null}
        onToggleStep={onToggleStep}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /Approval/i }));
    expect(onToggleStep).toHaveBeenCalledWith('step-2');
  });

  it('shows a cumulative token badge that carries across steps without usage (#200)', () => {
    const snapshotBase = {
      executionMode: 'local',
      skills: [],
      tools: [],
      toolInvocations: [],
      mcpServers: [],
      hooks: [],
      permissionLevel: 'read-only',
      allowedTools: [],
      deniedTools: [],
      subAgentsEnabled: false,
      stepArtifacts: [],
    };
    render(
      <StepTimeline
        steps={[
          {
            id: 'step-1',
            name: 'Draft',
            type: 'serviceTask',
            status: 'completed',
            runtimeSnapshot: {
              ...snapshotBase,
              tokenUsage: { inputTokens: 1000, outputTokens: 500 },
            },
          },
          { id: 'step-2', name: 'Wait', type: 'intermediateCatchEvent', status: 'completed' },
          {
            id: 'step-3',
            name: 'Implement',
            type: 'serviceTask',
            status: 'failed',
            runtimeSnapshot: {
              ...snapshotBase,
              tokenUsage: { inputTokens: 2000, outputTokens: 1500 },
            },
          },
        ]}
        expandedStepId="step-3"
        onToggleStep={vi.fn()}
      />,
    );

    // Step 1: 1.5K so far; step 2 carries it despite no own usage; step 3 reaches 5K.
    expect(screen.getAllByText('Σ 1.5K')).toHaveLength(2);
    expect(screen.getByText('Σ 5K')).toBeInTheDocument();
    // Expanded step shows the running total including failed-step usage.
    expect(screen.getByText(/Run total after this step: 3,000 in · 2,000 out/)).toBeInTheDocument();
  });

  it('shows agent identity and LLM activity inside expanded timeline steps', () => {
    render(
      <StepTimeline
        steps={[
          {
            id: 'step-llm',
            name: 'Reason about change',
            type: 'serviceTask',
            status: 'completed',
            runtimeSnapshot: {
              agentName: 'planner',
              executionMode: 'agent_sandboxed',
              skills: [],
              tools: [],
              toolInvocations: [],
              mcpServers: [],
              hooks: [],
              permissionLevel: 'read-only',
              allowedTools: [],
              deniedTools: [],
              subAgentsEnabled: false,
              stepArtifacts: [],
              modelTraces: [
                {
                  status: 'completed',
                  modelId: 'gpt-test',
                  startedAt: new Date().toISOString(),
                  completedAt: new Date().toISOString(),
                  elapsedMs: 850,
                  inputTokens: 123,
                  outputTokens: 45,
                  output: 'Visible planning output.',
                  failureReason: null,
                  toolCalls: [
                    {
                      id: 'call-1',
                      name: 'repo.search',
                      inputSummary: '{"q":"identity"}',
                    },
                  ],
                },
              ],
            },
          },
        ]}
        expandedStepId="step-llm"
        onToggleStep={vi.fn()}
      />,
    );

    expect(screen.getAllByTitle('Agent planner').length).toBeGreaterThan(0);
    expect(screen.getByText('LLM 1 trace')).toBeInTheDocument();
    expect(screen.getByText('LLM Activity')).toBeInTheDocument();
    expect(screen.getByText('Inference Signals')).toBeInTheDocument();
    expect(screen.getByText('Visible Output')).toBeInTheDocument();
    expect(screen.getByText('Visible planning output.')).toBeInTheDocument();
    expect(screen.getByText('repo.search')).toBeInTheDocument();
  });
});

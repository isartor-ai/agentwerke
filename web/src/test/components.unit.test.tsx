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
});

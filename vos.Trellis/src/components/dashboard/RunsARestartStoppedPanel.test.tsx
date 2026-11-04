import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { RunsARestartStoppedPanel } from './RunsARestartStoppedPanel';
import type { RunARestartStopped } from '../../pipeline/model';

const gauge: RunARestartStopped = {
  runId: 'run-gauge', pipelineName: 'Rainfall reading', startedUtc: '2026-11-04T10:00:00Z',
  reason: "'Read the gauge' was calling its service when the orchestrator stopped",
};
const level: RunARestartStopped = { runId: 'run-level', pipelineName: 'Reservoir level', startedUtc: '2026-11-04T09:00:00Z', reason: 'stopped' };

const accepted = () => Promise.resolve(true);
const row = (pipelineName: string) => screen.getByText(pipelineName).closest('li')!;

describe('RunsARestartStoppedPanel', () => {
  it('shows each run with its pipeline and the reason the restart gave, and both asks to a person who may change data', () => {
    render(<RunsARestartStoppedPanel runs={[gauge, level]} mayChangeData onStartAgain={accepted} onCarryOn={accepted} />);

    expect(screen.getByText('Runs a restart stopped')).toBeInTheDocument();
    expect(within(row('Rainfall reading')).getByText(gauge.reason)).toBeInTheDocument();
    expect(within(row('Reservoir level')).getByRole('button', { name: 'Start again' })).toBeEnabled();
    expect(within(row('Reservoir level')).getByRole('button', { name: 'Carry on' })).toBeEnabled();
  });

  it('shows the runs without either ask to a person who may not change data', () => {
    render(<RunsARestartStoppedPanel runs={[gauge]} mayChangeData={false} onStartAgain={accepted} onCarryOn={accepted} />);

    expect(screen.getByText('Rainfall reading')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it.each([
    ['Start again', 'onStartAgain'],
    ['Carry on', 'onCarryOn'],
  ] as const)('asks once for the run pressed, and %s stays unpressable once the orchestrator accepted', async (label, handler) => {
    const asks = { onStartAgain: vi.fn(accepted), onCarryOn: vi.fn(accepted) };
    render(<RunsARestartStoppedPanel runs={[gauge, level]} mayChangeData {...asks} />);

    fireEvent.click(within(row('Rainfall reading')).getByRole('button', { name: label }));

    await waitFor(() => expect(asks[handler]).toHaveBeenCalledWith('run-gauge'));
    for (const button of within(row('Rainfall reading')).getAllByRole('button')) expect(button).toBeDisabled();
    fireEvent.click(within(row('Rainfall reading')).getByRole('button', { name: label }));
    expect(asks[handler]).toHaveBeenCalledTimes(1);
    expect(within(row('Reservoir level')).getByRole('button', { name: label })).toBeEnabled();
  });

  it('offers the asks again when the orchestrator refused one', async () => {
    const refused = vi.fn(() => Promise.resolve(false));
    render(<RunsARestartStoppedPanel runs={[gauge]} mayChangeData onStartAgain={refused} onCarryOn={accepted} />);

    fireEvent.click(screen.getByRole('button', { name: 'Start again' }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Start again' })).toBeEnabled());
    expect(refused).toHaveBeenCalledTimes(1);
  });

  it('shows nothing when no run is waiting', () => {
    const { container } = render(<RunsARestartStoppedPanel runs={[]} mayChangeData onStartAgain={accepted} onCarryOn={accepted} />);

    expect(container).toBeEmptyDOMElement();
  });
});

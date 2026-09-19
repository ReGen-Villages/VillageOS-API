import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ActionWidget, Binding } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';

const mockRows = vi.fn();
const mockOptions = vi.fn();
// The rows a widget acts on are a state's members; what it asks for a row is chosen from a
// roster. Told apart by the binding, so one mock serves both reads.
vi.mock('../../../hooks/useDashboard', () => ({
  useBindings: (bindings: (Binding | undefined)[]) =>
    bindings.map((binding) => ({
      loading: false,
      error: false,
      value: binding ? (binding.kind === 'thingList' ? mockOptions() : mockRows()) : null,
    })),
}));

import { ApiError } from '../../../api/client';
import { ActionList } from './ActionList';

const JUDGE: ActionWidget = {
  type: 'action',
  title: 'Springs awaiting a verdict',
  label: 'name',
  rows: { kind: 'stateList', state: 'sampled', archetype: 'Spring' },
  shows: ['flow'],
  writes: {
    via: 'verdicts',
    archetype: 'Verdict',
    predicate: 'about',
    choices: [
      { label: 'Potable', target: 'Potable', viaPredicate: 'found' },
      { label: 'Unfit', target: 'Unfit', viaPredicate: 'found' },
    ],
  },
};

/** A widget whose press marks the row itself: no Thing minted, no reason named, and a value the
 *  row is asked for first. */
const ASSIGN: ActionWidget = {
  type: 'action',
  title: 'Waiting for a reading',
  rows: { kind: 'stateList', state: 'due', archetype: 'Spring' },
  asks: [{ key: 'reader', label: 'Reader', kind: 'choice', options: { kind: 'thingList', archetype: 'Person' } }],
  writes: { via: 'readings', choices: [{ label: 'Assign', act: 'assign' }] },
};

const mockPost = vi.fn();
const ctx = { reads: { fromService: (endpoint: string, body: unknown) => mockPost(endpoint, body) } } as unknown as ResolveContext;

describe('ActionList', () => {
  beforeEach(() => {
    mockRows.mockReset().mockReturnValue([{ id: 's1', name: 'SPRING-1', flow: 12 }]);
    mockOptions.mockReset().mockReturnValue([{ id: 'p1', name: 'Ada' }, { id: 'p2', name: 'Grace' }]);
    mockPost.mockReset().mockResolvedValue({ said: 'Verdict recorded for SPRING-1' });
  });

  it('offers every choice the spec names, beside what the row shows', () => {
    render(<ActionList widget={JUDGE} ctx={ctx} />);
    expect(screen.getByRole('button', { name: 'Potable' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Unfit' })).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
  });

  it('posts the row and the choice to the endpoint the spec names, and nothing naming who asked', async () => {
    render(<ActionList widget={JUDGE} ctx={ctx} />);
    fireEvent.click(screen.getByRole('button', { name: 'Potable' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1));
    expect(mockPost).toHaveBeenCalledWith('/api/endpoints/verdicts', { record: 'SPRING-1', reason: 'Potable' });
  });

  it('shows a refusal in the words the endpoint used, whether it refused with a status or with an answer', async () => {
    mockPost.mockRejectedValueOnce(new ApiError(409, JSON.stringify({ error: 'SPRING-1 was judged an hour ago' })));
    render(<ActionList widget={JUDGE} ctx={ctx} />);
    fireEvent.click(screen.getByRole('button', { name: 'Potable' }));
    expect(await screen.findByText('SPRING-1 was judged an hour ago')).toBeInTheDocument();

    mockPost.mockResolvedValueOnce({ error: 'no sample on record' });
    fireEvent.click(screen.getByRole('button', { name: 'Unfit' }));
    expect(await screen.findByText('no sample on record')).toBeInTheDocument();
  });

  it('says what the endpoint said once it has taken the press, and offers no second press on the row', async () => {
    render(<ActionList widget={JUDGE} ctx={ctx} />);
    fireEvent.click(screen.getByRole('button', { name: 'Potable' }));

    expect(await screen.findByText('Verdict recorded for SPRING-1')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Potable' })).toBeNull();
  });

  it('offers what a row is asked for from the roster the spec names, and waits for it before a press', async () => {
    render(<ActionList widget={ASSIGN} ctx={ctx} />);
    const reader = screen.getByLabelText('Reader');
    expect(screen.getByRole('option', { name: 'Grace' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Assign' })).toBeDisabled();

    fireEvent.change(reader, { target: { value: 'Ada' } });
    fireEvent.click(screen.getByRole('button', { name: 'Assign' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/endpoints/readings', { view: 'assign', record: 'SPRING-1', reader: 'Ada' }));
  });
});

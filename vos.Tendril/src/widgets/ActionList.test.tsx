import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ActionWidget, Binding } from '../types/dashboard';
import type { ResolveContext } from '../api/dashboardApi';

const mockRows = vi.fn();
const mockOptions = vi.fn();
const answered = new Map<Binding, unknown>();
// The rows a widget acts on are a state's members; what it asks for a row is chosen from a
// roster. Told apart by the binding, so one mock serves both reads.
vi.mock('../hooks/useBinding', () => ({
  useBindings: (bindings: (Binding | undefined)[]) =>
    bindings.map((binding) => ({
      loading: false,
      error: false,
      value: binding ? (answered.get(binding) ?? (binding.kind === 'thingList' ? mockOptions() : mockRows())) : null,
    })),
}));

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

/** A widget that administers rather than decides: the same row is pressed as often as needed, and
 *  it posts to a platform route named by its path. */
const ADMINISTER: ActionWidget = {
  type: 'action',
  title: 'Model access',
  rows: { kind: 'stateList', state: 'accounts', archetype: 'Account' },
  asks: [{ key: 'model', label: 'Model', kind: 'choice', options: { kind: 'thingList', archetype: 'Model' } }],
  writes: {
    via: '/api/auth/administration',
    repeatable: true,
    choices: [{ label: 'Grant', act: 'grant' }, { label: 'Revoke', act: 'revoke' }],
  },
};

const mockPost = vi.fn();
const mockWrote = vi.fn();
const context = {
  reads: { fromService: (endpoint: string, body: unknown) => mockPost(endpoint, body) },
  wrote: () => mockWrote(),
} as unknown as ResolveContext;

describe('ActionList', () => {
  beforeEach(() => {
    mockRows.mockReset().mockReturnValue([{ id: 's1', name: 'SPRING-1', flow: 12 }]);
    mockOptions.mockReset().mockReturnValue([{ id: 'p1', name: 'Ada' }, { id: 'p2', name: 'Grace' }]);
    mockPost.mockReset().mockResolvedValue({ said: 'Verdict recorded for SPRING-1' });
    mockWrote.mockReset();
  });

  it('tells the page a press was taken, so what the page reads from the broker is read again', async () => {
    render(<ActionList widget={JUDGE} context={context} />);
    fireEvent.click(screen.getByRole('button', { name: 'Potable' }));

    await screen.findByText('Verdict recorded for SPRING-1');
    expect(mockWrote).toHaveBeenCalledTimes(1);
  });

  it('tells the page nothing of a press the endpoint refused', async () => {
    mockPost.mockResolvedValueOnce({ error: 'no sample on record' });
    render(<ActionList widget={JUDGE} context={context} />);
    fireEvent.click(screen.getByRole('button', { name: 'Potable' }));

    await screen.findByText('no sample on record');
    expect(mockWrote).not.toHaveBeenCalled();
  });

  it('keeps a row pressable where the acts are repeatable, with the last answer beside it, and posts to a platform route as written', async () => {
    mockRows.mockReturnValue([{ id: 'u1', name: 'ada' }]);
    mockPost.mockResolvedValue({ said: 'ada may now enter Site A' });
    render(<ActionList widget={ADMINISTER} context={context} />);
    fireEvent.change(screen.getByLabelText('Model'), { target: { value: 'Ada' } });
    fireEvent.click(screen.getByRole('button', { name: 'Grant' }));

    expect(await screen.findByText('ada may now enter Site A')).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledWith('/api/auth/administration', { view: 'grant', record: 'ada', recordId: 'u1', model: 'Ada' });
    expect(screen.getByRole('button', { name: 'Grant' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Revoke' })).toBeInTheDocument();

    mockPost.mockResolvedValue({ said: 'ada may no longer enter Site A' });
    fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));
    expect(await screen.findByText('ada may no longer enter Site A')).toBeInTheDocument();
    expect(screen.queryByText('ada may now enter Site A')).toBeNull();
  });

  it('offers every choice the specification names, beside what the row shows', () => {
    render(<ActionList widget={JUDGE} context={context} />);
    expect(screen.getByRole('button', { name: 'Potable' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Unfit' })).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
  });

  it('posts the row and the choice to the endpoint the specification names, and nothing naming who asked', async () => {
    render(<ActionList widget={JUDGE} context={context} />);
    fireEvent.click(screen.getByRole('button', { name: 'Potable' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1));
    expect(mockPost).toHaveBeenCalledWith('/api/endpoints/verdicts', { record: 'SPRING-1', reason: 'Potable' });
  });

  it('shows a refusal in the words the endpoint used, whether it refused with a status or with an answer', async () => {
    mockPost.mockRejectedValueOnce(new Error('SPRING-1 was judged an hour ago'));
    render(<ActionList widget={JUDGE} context={context} />);
    fireEvent.click(screen.getByRole('button', { name: 'Potable' }));
    expect(await screen.findByText('SPRING-1 was judged an hour ago')).toBeInTheDocument();

    mockPost.mockResolvedValueOnce({ error: 'no sample on record' });
    fireEvent.click(screen.getByRole('button', { name: 'Unfit' }));
    expect(await screen.findByText('no sample on record')).toBeInTheDocument();
  });

  it('says what the endpoint said once it has taken the press, and offers no second press on the row', async () => {
    render(<ActionList widget={JUDGE} context={context} />);
    fireEvent.click(screen.getByRole('button', { name: 'Potable' }));

    expect(await screen.findByText('Verdict recorded for SPRING-1')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Potable' })).toBeNull();
  });

  it('offers what a row is asked for from the roster the specification names, and waits for it before a press', async () => {
    render(<ActionList widget={ASSIGN} context={context} />);
    const reader = screen.getByLabelText('Reader');
    expect(screen.getByRole('option', { name: 'Grace' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Assign' })).toBeDisabled();

    fireEvent.change(reader, { target: { value: 'Ada' } });
    fireEvent.click(screen.getByRole('button', { name: 'Assign' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/endpoints/readings', { view: 'assign', record: 'SPRING-1', reader: 'Ada' }));
  });

  describe('with choices the model holds', () => {
    const verdicts: Binding = { kind: 'thingList', archetype: 'Verdict' };
    const judged: Binding = { kind: 'stateList', state: 'judged', archetype: 'Spring' };
    const REVIEW: ActionWidget = {
      type: 'action',
      rows: { kind: 'stateList', state: 'sampled', archetype: 'Spring' },
      writes: {
        choicesFrom: { from: verdicts, predicate: 'judgedAs' },
        stamps: 'judgedAt',
        choices: [
          {
            label: 'Close the spring',
            target: 'closed',
            via: '/api/springs/close',
            asks: [{ key: 'until', label: 'Closed until' }],
          },
        ],
      },
      decided: { rows: judged, decision: 'verdict' },
    };

    const mockRelate = vi.fn();
    const mockStamp = vi.fn();
    const writing = {
      ...context,
      index: { predicateNameToId: new Map([['judgedAs', 'predicate-judgedAs']]) },
      reads: { ...context.reads, relate: mockRelate, stampTime: mockStamp },
    } as unknown as ResolveContext;

    beforeEach(() => {
      answered.clear();
      answered.set(verdicts, [
        { id: 'v1', name: 'potable', wording: JSON.stringify({ en: 'Potable', nl: 'Drinkbaar' }) },
        { id: 'v2', name: 'unfit' },
        { id: 'v3', name: 'closed' },
      ]);
      answered.set(judged, [{ id: 's9', name: 'SPRING-9', verdict: 'unfit' }]);
      mockRelate.mockReset().mockResolvedValue(undefined);
      mockStamp.mockReset().mockResolvedValue(undefined);
    });

    it('offers one choice for each Thing the binding returns, by its wording or else its name', () => {
      render(<ActionList widget={REVIEW} context={writing} />);
      expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['Potable', 'unfit', 'Close the spring']);
    });

    it('relates the row to the Thing pressed, then stamps the time on the row, and posts nothing', async () => {
      render(<ActionList widget={REVIEW} context={writing} />);
      fireEvent.click(screen.getByRole('button', { name: 'unfit' }));

      await waitFor(() => expect(mockStamp).toHaveBeenCalledWith('s1', 'judgedAt', expect.any(Date)));
      expect(mockRelate).toHaveBeenCalledWith('s1', 'predicate-judgedAs', 'v2');
      expect(mockPost).not.toHaveBeenCalled();
      expect(mockWrote).toHaveBeenCalledTimes(1);
    });

    it('stamps nothing where the relationship was refused', async () => {
      mockRelate.mockRejectedValueOnce(new Error('SPRING-1 is already judged'));
      render(<ActionList widget={REVIEW} context={writing} />);
      fireEvent.click(screen.getByRole('button', { name: 'unfit' }));

      expect(await screen.findByText('SPRING-1 is already judged')).toBeInTheDocument();
      expect(mockStamp).not.toHaveBeenCalled();
    });

    it('asks a listed choice its own questions and posts it to its own route, in place of the Thing it names', async () => {
      render(<ActionList widget={REVIEW} context={writing} />);
      fireEvent.click(screen.getByRole('button', { name: 'Close the spring' }));
      const confirm = screen.getByRole('button', { name: 'Close the spring' });
      expect(confirm).toBeDisabled();

      fireEvent.change(screen.getByLabelText('Closed until'), { target: { value: 'spring thaw' } });
      fireEvent.click(confirm);

      await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/springs/close', { record: 'SPRING-1', recordId: 's1', reason: 'closed', until: 'spring thaw' }));
      expect(mockRelate).not.toHaveBeenCalled();
      await waitFor(() => expect(mockStamp).toHaveBeenCalledWith('s1', 'judgedAt', expect.any(Date)));
    });

    it('goes back to every choice when asking is cancelled', () => {
      render(<ActionList widget={REVIEW} context={writing} />);
      fireEvent.click(screen.getByRole('button', { name: 'Close the spring' }));
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

      expect(screen.getByRole('button', { name: 'unfit' })).toBeInTheDocument();
      expect(screen.queryByLabelText('Closed until')).toBeNull();
    });

    it('draws the rows already decided only behind the switch, with what was decided and nothing to press', () => {
      render(<ActionList widget={REVIEW} context={writing} />);
      expect(screen.queryByText('SPRING-9')).toBeNull();

      fireEvent.click(screen.getByLabelText('Show decided'));
      const decidedRow = screen.getByText('SPRING-9').closest('div')!;
      expect(decidedRow).toHaveTextContent('unfit');
      expect(decidedRow.querySelector('button')).toBeNull();
    });
  });
});

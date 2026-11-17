import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

vi.mock('../hooks/useSse', () => ({ useSubscription: () => {} }));

const temporal = vi.hoisted(() => ({
  getModelMutations: vi.fn(), getThingMutations: vi.fn(), getRelationshipMutations: vi.fn(),
}));
const thingReads = vi.hoisted(() => ({ getAll: vi.fn() }));
const relationshipReads = vi.hoisted(() => ({ getAll: vi.fn() }));
const toast = vi.hoisted(() => ({ error: vi.fn(), success: vi.fn() }));
vi.mock('../api/temporalApi', () => ({ temporalApi: temporal }));
vi.mock('../api/thingApi', () => ({ thingApi: thingReads }));
vi.mock('../api/relationshipApi', () => ({ relationshipApi: relationshipReads }));
vi.mock('../components/common/toastStore', () => ({ toast }));

import { TemporalPage } from './TemporalPage';
import { useModelStore } from '../stores/modelStore';

const CHANGE = { Timestamp: '2026-11-01T10:00:00Z', PropertyName: 'flowRate', OldValue: 4, NewValue: 7 };

beforeEach(() => {
  vi.clearAllMocks();
  thingReads.getAll.mockResolvedValue([]);
  relationshipReads.getAll.mockResolvedValue([]);
});

const TAB_LABELS = [
  'Mutations', 'Thing Mutations', 'Relationship Mutations', 'Snapshot', 'Property History', 'State Query',
];

describe('the temporal page on a narrow screen', () => {
  it('scrolls its tabs sideways rather than cutting the last ones off', () => {
    render(<TemporalPage />);

    expect(screen.getByRole('button', { name: 'Mutations' }).parentElement).toHaveClass('overflow-x-auto');
  });

  it('wraps each query row, so no field is pushed past the edge', () => {
    const { container } = render(<TemporalPage />);

    for (const label of TAB_LABELS) {
      fireEvent.click(screen.getByRole('button', { name: label }));
      const rows = container.querySelectorAll('.items-end');
      expect(rows.length, label).toBeGreaterThan(0);
      for (const row of rows) expect(row, label).toHaveClass('flex-wrap');
    }
  });
});

describe('the mutation panels', () => {
  const thing = (Id: string, Name: string) => ({ Id, Name, Properties: {} });

  it("read the whole model's changes over the time range and list them under each Thing", async () => {
    temporal.getModelMutations.mockResolvedValue({
      StartTime: '2026-11-01T00:00:00Z', EndTime: '2026-11-02T00:00:00Z', TotalMutations: 1,
      ThingMutations: { a: { ObjectId: 'a', ObjectName: 'Reservoir', StartTime: '', EndTime: '', Mutations: [CHANGE] } },
    });
    render(<TemporalPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Query' }));

    expect(await screen.findByText('Reservoir')).toBeInTheDocument();
    expect(screen.getByText('flowRate')).toBeInTheDocument();
    expect(temporal.getModelMutations).toHaveBeenCalledWith(undefined, undefined);
  });

  it('say so when the whole model changed nothing in the range', async () => {
    temporal.getModelMutations.mockResolvedValue({ StartTime: '', EndTime: '', TotalMutations: 0, ThingMutations: {} });
    render(<TemporalPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Query' }));

    expect(await screen.findByText('No mutations in this time range.')).toBeInTheDocument();
  });

  it("read one Thing's changes only once a Thing is chosen", async () => {
    thingReads.getAll.mockResolvedValue([thing('t1', 'Reservoir')]);
    temporal.getThingMutations.mockResolvedValue({ ObjectId: 't1', ObjectName: 'Reservoir', StartTime: '', EndTime: '', Mutations: [CHANGE] });
    render(<TemporalPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Thing Mutations' }));
    await screen.findByRole('option', { name: 'Reservoir' });

    expect(screen.getByRole('button', { name: 'Query' })).toBeDisabled();
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 't1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Query' }));

    expect(await screen.findByText('1 mutation', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('flowRate')).toBeInTheDocument();
    expect(temporal.getThingMutations).toHaveBeenCalledWith('t1', undefined, undefined);
  });

  it("label each relationship from the loaded Things and read the chosen one's changes", async () => {
    useModelStore.setState({ things: [thing('a', 'Pump'), thing('p', 'feeds'), thing('b', 'Tank')] as never });
    relationshipReads.getAll.mockResolvedValue([{ Id: 'r1', SubjectId: 'a', PredicateId: 'p', TargetId: 'b', Properties: {} }]);
    temporal.getRelationshipMutations.mockResolvedValue({
      RelationshipId: 'r1', RelationshipName: '', SubjectId: 'a', PredicateId: 'p', TargetId: 'b', StartTime: '', EndTime: '', Mutations: [CHANGE],
    });
    render(<TemporalPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Relationship Mutations' }));

    fireEvent.change(screen.getByRole('combobox'), { target: { value: (await screen.findByRole('option', { name: 'Pump feeds Tank' })).getAttribute('value') } });
    fireEvent.click(screen.getByRole('button', { name: 'Query' }));

    expect(await screen.findByText('flowRate')).toBeInTheDocument();
    expect(temporal.getRelationshipMutations).toHaveBeenCalledWith('r1', undefined, undefined);
  });

  it('show the reason when reading the changes fails', async () => {
    temporal.getModelMutations.mockRejectedValue(new Error('broker unreachable'));
    render(<TemporalPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Query' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('broker unreachable'));
  });
});

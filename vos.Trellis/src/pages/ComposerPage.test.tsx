import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { VosThing, VosRelationship } from '../types/vos';

vi.mock('../hooks/useSse', () => ({
  useSse: () => ({ connected: true, on: () => () => {} }),
  useSubscription: () => {},
}));
vi.mock('../api/stateApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/stateApi')>()),
  stateApi: { getThingsInState: vi.fn(), getStateTransitions: vi.fn() },
}));
vi.mock('../api/rangeApi', () => ({ rangeApi: { getAll: vi.fn() } }));
vi.mock('../api/modelApi', () => ({ modelApi: { applyFragment: vi.fn(), getAtTime: vi.fn() } }));
vi.mock('../hooks/useDeclaredPropertyTypes', () => ({ useDeclaredPropertyTypes: () => ({}) }));

import { stateApi } from '../api/stateApi';
import { rangeApi } from '../api/rangeApi';
import { modelApi } from '../api/modelApi';
import { useModelStore } from '../stores/modelStore';
import { ComposerPage } from './ComposerPage';

function t(Id: string, Name: string, Properties: Record<string, unknown> = {}, IsArchetype = false): VosThing {
  return { Id, Name, Properties, IsArchetype };
}
function r(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

/** Two springs feeding a reservoir, and one page the seed already wrote. */
function seedStore() {
  useModelStore.setState({
    things: [
      t('is', 'is'),
      t('feeds', 'feeds'),
      t('dashboard', 'Dashboard', {}, true),
      t('seeded', 'Water overview', { spec: JSON.stringify({ title: 'Water overview', sections: [{ widgets: [] }] }) }),
      t('spring', 'Spring', { flow: 0 }, true),
      t('reservoir', 'Reservoir', { capacity: 0 }, true),
      t('s1', 'SPRING-1', { flow: 12.5 }),
      t('s2', 'SPRING-2', { flow: 3 }),
      t('r1', 'RESERVOIR-1', { capacity: 900 }),
    ],
    relationships: [
      r('i0', 'seeded', 'is', 'dashboard'),
      r('i1', 's1', 'is', 'spring'),
      r('i2', 's2', 'is', 'spring'),
      r('i3', 'r1', 'is', 'reservoir'),
      r('e1', 's1', 'feeds', 'r1'),
      r('e2', 's2', 'feeds', 'r1'),
    ],
    loaded: true,
  });
}

function open() {
  return render(
    <MemoryRouter>
      <ComposerPage />
    </MemoryRouter>,
  );
}

function chooseKind(kind: string) {
  fireEvent.change(screen.getByLabelText('Kind of Thing'), { target: { value: kind } });
}

describe('ComposerPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('crypto', { randomUUID: () => 'minted-id' });
    vi.mocked(rangeApi.getAll).mockResolvedValue({ ThingId: 'spring', ThingName: 'Spring', OwnRanges: [{ Name: 'dry' }] as never, InheritedRanges: [] });
    vi.mocked(stateApi.getThingsInState).mockResolvedValue({ StateName: 'dry', Things: [{ Id: 's2', Name: 'SPRING-2' }] });
    vi.mocked(modelApi.applyFragment).mockResolvedValue({ thingsCreated: 1, thingsUpdated: 0, relationshipsCreated: 1, things: [] });
    seedStore();
  });

  it('offers the kinds with instances, and for a chosen kind its properties, links and states', async () => {
    open();
    const kinds = within(screen.getByLabelText('Kind of Thing')).getAllByRole('option').map((o) => o.textContent);
    expect(kinds).toContain('Spring');
    expect(kinds).toContain('Reservoir');
    expect(kinds).not.toContain('SPRING-1');

    chooseKind('Spring');

    expect(screen.getByRole('button', { name: /^flow/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /feeds → Reservoir/ })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'dry' })).toBeInTheDocument();
  });

  it('draws the table through the dashboard’s own table with the chosen columns, and says what a path costs', async () => {
    open();
    chooseKind('Spring');
    fireEvent.click(screen.getByRole('button', { name: /^flow/ }));
    fireEvent.click(screen.getByRole('button', { name: /feeds → Reservoir/ }));

    expect(await screen.findByText('SPRING-1')).toBeInTheDocument();
    expect(screen.getByText('12.5')).toBeInTheDocument();
    expect(screen.getAllByText('RESERVOIR-1').length).toBeGreaterThan(0);
    expect(screen.getByText('1 hop')).toBeInTheDocument();
  });

  it('keeps only the rows holding the chosen state', async () => {
    open();
    chooseKind('Spring');
    await screen.findByRole('button', { name: 'dry' });
    fireEvent.change(screen.getByLabelText('In state'), { target: { value: 'dry' } });

    expect(await screen.findByText('SPRING-2')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('SPRING-1')).toBeNull());
  });

  it('refuses a name a page already carries before anything is written', async () => {
    open();
    chooseKind('Spring');
    fireEvent.change(screen.getByLabelText('Page name'), { target: { value: 'Water overview' } });

    expect(screen.getByText(/already carries/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Keep as a page' })).toBeDisabled();
    expect(modelApi.applyFragment).not.toHaveBeenCalled();
  });

  it('keeps the page as one fragment under the name given', async () => {
    open();
    chooseKind('Spring');
    fireEvent.click(screen.getByRole('button', { name: /^flow/ }));
    fireEvent.change(screen.getByLabelText('Page name'), { target: { value: 'Springs by flow' } });
    fireEvent.click(screen.getByRole('button', { name: 'Keep as a page' }));

    await waitFor(() => expect(modelApi.applyFragment).toHaveBeenCalledTimes(1));
    const fragment = JSON.parse(vi.mocked(modelApi.applyFragment).mock.calls[0][0]);
    expect(fragment.Things[0].Name).toBe('Springs by flow');
    const spec = JSON.parse(fragment.Things[0].Properties.spec.value);
    expect(spec.composed).toMatchObject({ kind: 'Spring', columns: [{ source: 'property', name: 'flow' }] });
    expect(fragment.Relationships).toEqual([{ Subject: 'minted-id', Predicate: 'is', Target: 'dashboard' }]);
  });

  it('takes the state choices off the composition when a moment is chosen, and says why', async () => {
    vi.mocked(modelApi.getAtTime).mockImplementation(() => new Promise(() => {}));
    open();
    chooseKind('Spring');
    fireEvent.click(await screen.findByRole('button', { name: 'dry' }));
    expect(screen.getByText('dry', { selector: 'li span' })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Moment'), { target: { value: '2026-09-01T09:00' } });

    expect(screen.queryByText('dry', { selector: 'li span' })).toBeNull();
    expect(screen.getByLabelText('In state')).toBeDisabled();
    expect(screen.getByText(/no read answers a state at an instant/i)).toBeInTheDocument();
  });
});

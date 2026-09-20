import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import type { VosThing, VosRelationship } from '../types/vos';

const declaredSubscriptions: unknown[] = [];
const streamHandlers = new Map<string, ((data?: unknown) => void)[]>();
vi.mock('../hooks/useSse', () => ({
  useSse: () => ({
    connected: true,
    on: (event: string, handler: (data?: unknown) => void) => {
      streamHandlers.set(event, [...(streamHandlers.get(event) ?? []), handler]);
      return () => streamHandlers.set(event, (streamHandlers.get(event) ?? []).filter((held) => held !== handler));
    },
  }),
  useSubscription: (selector: unknown) => { declaredSubscriptions.push(selector); },
}));
vi.mock('../api/stateApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/stateApi')>()),
  stateApi: { getThingsInState: vi.fn() },
}));
vi.mock('../api/client', async (importOriginal) => {
  const original = await importOriginal<typeof import('../api/client')>();
  return { ...original, apiClient: Object.assign(original.apiClient, { post: vi.fn() }) };
});

import { apiClient } from '../api/client';
import { stateApi } from '../api/stateApi';
import { usePlatformPagesStore } from '../stores/platformPagesStore';
import { subscriptionForSpec } from '../api/dashboardSubscription';
import type { DashboardSpec } from '../types/dashboard';
import { useModelStore } from '../stores/modelStore';
import { useUiStore } from '../stores/uiStore';
import { OperationsPage } from './OperationsPage';
import { act } from '@testing-library/react';

const SPEC = {
  title: 'Ops',
  subtitle: 'test',
  compare: { label: 'site', archetype: 'Village' },
  sections: [
    {
      title: 'Headline',
      layout: 'kpi-strip',
      widgets: [
        {
          type: 'kpi',
          title: 'Self-sufficiency rate',
          value: { kind: 'property', thing: '$scope', property: 'self_sufficiency_rate' },
          format: 'decimal1',
          unit: '%',
          target: 99,
        },
      ],
    },
    {
      title: 'Pipeline',
      layout: 'single',
      widgets: [
        {
          type: 'funnel',
          title: 'Plots by stage',
          stages: [{ label: 'Harvested', color: '#10b981', count: { kind: 'stateCount', state: 'harvested' } }],
        },
      ],
    },
    {
      title: 'Scorecard',
      layout: 'single',
      widgets: [
        {
          type: 'leaderboard',
          title: 'Site scorecard',
          labelKey: 'name',
          entities: { kind: 'compareEntities', properties: ['self_sufficiency_rate'] },
          metrics: [
            { key: 'self_sufficiency_rate', label: 'Self-sufficiency', format: 'decimal1', direction: 'up-good', weight: 1, best: 100, worst: 90 },
          ],
        },
      ],
    },
  ],
};

function seedStore() {
  const t = (Id: string, Name: string, Properties: Record<string, unknown> = {}): VosThing => ({ Id, Name, Properties });
  const things: VosThing[] = [
    t('is', 'is'),
    t('arch-dash', 'Dashboard'),
    t('arch-vil', 'Village'),
    t('dash1', 'Operations Dashboard', { spec: JSON.stringify(SPEC) }),
    t('vil1', 'V-1', { self_sufficiency_rate: 98.9 }),
    t('vil2', 'V-2', { self_sufficiency_rate: 94.1 }),
  ];
  const r = (s: string, tg: string): VosRelationship => ({
    Id: `${s}-is-${tg}`,
    Name: `${s} is ${tg}`,
    SubjectId: s,
    PredicateId: 'is',
    TargetId: tg,
    Properties: {},
  });
  useModelStore.setState({
    things,
    relationships: [r('dash1', 'arch-dash'), r('vil1', 'arch-vil'), r('vil2', 'arch-vil')],
    loaded: true,
  });
}

function ShownPath() {
  return <span data-testid="path">{useLocation().pathname}</span>;
}

function renderAt(path = '/operations') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/operations" element={<OperationsPage />} />
        <Route path="/operations/:dashboardKey" element={<OperationsPage />} />
      </Routes>
      <ShownPath />
    </MemoryRouter>,
  );
}

describe('OperationsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    declaredSubscriptions.length = 0;
    vi.mocked(stateApi.getThingsInState).mockResolvedValue({ StateName: 'harvested', Count: 2 });
    seedStore();
  });

  it('renders the model-resident dashboard title + sections', () => {
    renderAt();
    expect(screen.getByText('Ops')).toBeInTheDocument();
    expect(screen.getByText('Headline')).toBeInTheDocument();
    expect(screen.getByText('Plots by stage')).toBeInTheDocument();
    expect(screen.getByText('Harvested')).toBeInTheDocument();
  });

  it('offers a scope switcher for the compare archetype', () => {
    renderAt();
    expect(screen.getByText('All sites')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'V-1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'V-2' })).toBeInTheDocument();
  });

  it('resolves a $scope KPI (averaged across sites for "All")', async () => {
    renderAt();
    expect(await screen.findByText('96.5')).toBeInTheDocument(); // (98.9 + 94.1) / 2
  });

  it('re-resolves the KPI when a specific site is selected', async () => {
    renderAt();
    await screen.findByText('96.5');
    fireEvent.click(screen.getByRole('button', { name: 'V-1' }));
    expect(await screen.findByText('98.9')).toBeInTheDocument();
  });

  it('resolves a stateCount funnel bar from the number the state endpoint gave', async () => {
    renderAt();
    expect(await screen.findByText('2')).toBeInTheDocument();
    expect(stateApi.getThingsInState).toHaveBeenCalledWith('harvested', expect.objectContaining({ countOnly: true }));
  });

  it('ranks sites in the leaderboard with the winner marked', async () => {
    renderAt();
    expect(await screen.findByText('🏆')).toBeInTheDocument();
    // V-1 / V-2 appear in both the scope switcher and the leaderboard row.
    expect(screen.getAllByText('V-1').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('V-2').length).toBeGreaterThanOrEqual(1);
  });

  it('shows guidance when the model has no Dashboard config', () => {
    useModelStore.setState({ things: [], relationships: [], loaded: true });
    renderAt();
    expect(screen.getByText('No dashboard configured')).toBeInTheDocument();
  });

  // The store this page reads holds nothing but what its own spec asks for — every figure above is
  // resolved from that set. What makes the set arrive is the page saying so.
  it('declares the subscription its spec describes', () => {
    renderAt();

    expect(declaredSubscriptions).toContainEqual(subscriptionForSpec(SPEC as DashboardSpec, null));
  });

  it('names the selected entity in what it declares', async () => {
    renderAt();
    await screen.findByText('96.5');

    fireEvent.click(screen.getByRole('button', { name: 'V-1' }));

    expect(declaredSubscriptions).toContainEqual(subscriptionForSpec(SPEC as DashboardSpec, 'vil1'));
  });
});

describe('OperationsPage addressing (Story 6582)', () => {
  const ROSTER_SPEC = { title: 'Roster', sections: [{ title: 'Rows', widgets: [] }] };

  beforeEach(() => {
    vi.clearAllMocks();
    seedStore();
    useModelStore.setState((s) => ({
      things: [...s.things, { Id: 'dash2', Name: 'Roster', Properties: { spec: JSON.stringify(ROSTER_SPEC) } }],
      relationships: [
        ...s.relationships,
        { Id: 'dash2-is', Name: 'dash2 is arch-dash', SubjectId: 'dash2', PredicateId: 'is', TargetId: 'arch-dash', Properties: {} },
      ],
    }));
  });

  it('renders the dashboard its address names, not the first one', () => {
    renderAt('/operations/roster');

    expect(screen.getByText('Roster')).toBeInTheDocument();
    expect(screen.queryByText('Ops')).toBeNull();
  });

  it('settles the bare operations address on the first dashboard', () => {
    renderAt();

    expect(screen.getByTestId('path').textContent).toBe('/operations/operations-dashboard');
    expect(screen.getByText('Ops')).toBeInTheDocument();
  });

  it('settles an address naming no known dashboard on the first one', () => {
    renderAt('/operations/a-dashboard-since-renamed');

    expect(screen.getByTestId('path').textContent).toBe('/operations/operations-dashboard');
    expect(screen.getByText('Ops')).toBeInTheDocument();
  });
});

describe('OperationsPage section widths (Bug 6671)', () => {
  const WIDTH_SPEC = {
    title: 'Widths',
    sections: [
      {
        title: 'Full width',
        layout: 'single',
        widgets: [{ type: 'kpi', title: 'One', value: { kind: 'const', value: 1 } }],
      },
      {
        title: 'Two columns',
        layout: 'split',
        widths: [2, 1],
        widgets: [
          { type: 'kpi', title: 'Left', value: { kind: 'const', value: 1 } },
          { type: 'kpi', title: 'Right', value: { kind: 'const', value: 2 } },
        ],
      },
    ],
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('matchMedia', (media: string) => ({
      matches: true,
      media,
      addEventListener: () => {},
      removeEventListener: () => {},
    }));
    useModelStore.setState({
      things: [
        { Id: 'is', Name: 'is', Properties: {} },
        { Id: 'arch-dash', Name: 'Dashboard', Properties: {} },
        { Id: 'dash1', Name: 'Widths', Properties: { spec: JSON.stringify(WIDTH_SPEC) } },
      ],
      relationships: [
        { Id: 'dash1-is', Name: 'dash1 is arch-dash', SubjectId: 'dash1', PredicateId: 'is', TargetId: 'arch-dash', Properties: {} },
      ],
      loaded: true,
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  function sectionGrids() {
    const { container } = renderAt('/operations/widths');
    return Array.from(container.querySelectorAll<HTMLElement>('section > div.grid'));
  }

  it('holds a full-width section to the width it was given', () => {
    expect(sectionGrids()[0].style.gridTemplateColumns).toBe('minmax(0, 1fr)');
  });

  it('holds each column of a two-column section to the width it was given', () => {
    expect(sectionGrids()[1].style.gridTemplateColumns).toBe('minmax(0, 2fr) minmax(0, 1fr)');
  });

  it('stops a card growing past its column, whatever it holds', () => {
    for (const grid of sectionGrids()) {
      for (const card of Array.from(grid.children)) {
        expect(card.className).toContain('min-w-0');
      }
    }
  });
});

// Story #6477: a spec is model data and can be authored wrong. Every one of these draws something a
// reader can act on, rather than an empty page that looks like a model with nothing in it.
describe('OperationsPage on a spec authored wrong', () => {
  function publish(name: string, spec: string) {
    useModelStore.setState((s) => ({
      things: [...s.things, { Id: name, Name: name, Properties: { spec } }],
      relationships: [
        ...s.relationships,
        { Id: `${name}-is`, Name: `${name} is arch-dash`, SubjectId: name, PredicateId: 'is', TargetId: 'arch-dash', Properties: {} },
      ],
    }));
  }

  beforeEach(() => {
    vi.clearAllMocks();
    seedStore();
  });

  it('names the dashboard whose spec it could not read', () => {
    publish('Unreadable', '{ "title": "Half a spec"');

    renderAt('/operations/unreadable');

    expect(screen.getByText('Unreadable could not be read')).toBeInTheDocument();
  });

  it('tells a spec it could not read apart from a model publishing no dashboard', () => {
    publish('Unreadable', 'not a specification at all');

    renderAt('/operations/unreadable');

    expect(screen.queryByText('No dashboard configured')).toBeNull();
  });

  it('says an authored view holding no sections is empty', () => {
    publish('Empty', JSON.stringify({ title: 'Nothing yet', sections: [] }));

    renderAt('/operations/empty');

    expect(screen.getByText('Nothing yet')).toBeInTheDocument();
    expect(screen.getByText(/lists no sections/)).toBeInTheDocument();
  });

  it('draws the widgets it knows and names the one kind it does not', async () => {
    publish('Mixed', JSON.stringify({
      title: 'Mixed',
      sections: [{
        title: 'Both',
        layout: 'single',
        widgets: [
          { type: 'kpi', title: 'A figure the client draws', value: { kind: 'const', value: 7 } },
          { type: 'sankey', title: 'A kind it does not' },
        ],
      }],
    }));

    renderAt('/operations/mixed');

    expect(await screen.findByText('7')).toBeInTheDocument();
    expect(screen.getByText('A figure the client draws')).toBeInTheDocument();
    expect(screen.getByText(/sankey/)).toBeInTheDocument();
  });
});

// One open dashboard asked the platform tens of times a second on a running model: every live
// event re-resolved every widget, and property changes are the highest-rate event there is.
describe('what a live event makes the page ask again', () => {
  const stateReads = () => vi.mocked(stateApi.getThingsInState).mock.calls.length;

  function arrived(event: string, times: number, data: unknown = {}): void {
    for (let count = 0; count < times; count += 1) streamHandlers.get(event)?.forEach((handler) => handler(data));
  }

  /** The stream's own beat: the states counter the model-data hook moves on a StatesChanged. */
  function statesMoved(states: string[]): void {
    useUiStore.getState().statesMoved(states);
    arrived('StatesChanged', 1, { entityId: 'p1', currentStates: states });
  }

  async function pastTheDebounce(): Promise<void> {
    await act(async () => { await vi.advanceTimersByTimeAsync(1000); });
  }

  beforeEach(() => {
    vi.clearAllMocks();
    streamHandlers.clear();
    useUiStore.setState({ stateVersions: {} });
    vi.mocked(stateApi.getThingsInState).mockResolvedValue({ StateName: 'harvested', Count: 2 });
    seedStore();
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => { vi.useRealTimers(); });

  function withCadence(): void {
    useModelStore.setState((s) => ({
      things: s.things.map((thing) =>
        thing.Id === 'dash1' ? { ...thing, Properties: { spec: JSON.stringify({ ...SPEC, refreshSeconds: 15 }) } } : thing),
    }));
  }

  it('re-reads a figure from the model on a burst of property changes, and asks the platform nothing', async () => {
    withCadence();
    renderAt();
    expect(await screen.findByText('96.5')).toBeInTheDocument();
    expect(await screen.findByText('2')).toBeInTheDocument();
    const before = stateReads();

    await act(async () => {
      arrived('PropertyChanged', 200);
      useModelStore.getState().applyBatch({ thingPropertyUpdates: [{ id: 'vil1', path: 'self_sufficiency_rate', value: 99.9 }] });
    });
    await pastTheDebounce();

    expect(await screen.findByText('97.0')).toBeInTheDocument();
    expect(stateReads()).toBe(before);
  });

  it('asks the platform once more when a state it counts moves', async () => {
    withCadence();
    renderAt();
    expect(await screen.findByText('2')).toBeInTheDocument();
    const before = stateReads();

    await act(async () => { statesMoved(['harvested']); });
    await pastTheDebounce();

    expect(stateReads()).toBe(before + 1);
  });

  it('asks the platform nothing when a state it does not read moves', async () => {
    withCadence();
    renderAt();
    expect(await screen.findByText('2')).toBeInTheDocument();
    const before = stateReads();

    await act(async () => { statesMoved(['planted']); });
    await pastTheDebounce();

    expect(stateReads()).toBe(before);
  });

  it('asks the platform again on the cadence the spec states', async () => {
    withCadence();
    renderAt();
    expect(await screen.findByText('2')).toBeInTheDocument();
    const before = stateReads();

    await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });

    expect(stateReads()).toBe(before + 1);
  });

  // The event that takes a Thing out of a state names the states it still holds and never the one it
  // left, so a page stating no cadence keeps the live event as the beat its figures fall back on.
  it('keeps the live event as the beat on a page stating no cadence', async () => {
    renderAt();
    expect(await screen.findByText('2')).toBeInTheDocument();
    const before = stateReads();

    await act(async () => { arrived('PropertyChanged', 200); });
    await pastTheDebounce();

    expect(stateReads()).toBe(before + 1);
  });

  it('asks every platform-answered figure again when the model is replaced', async () => {
    withCadence();
    renderAt();
    expect(await screen.findByText('2')).toBeInTheDocument();
    const before = stateReads();

    await act(async () => { arrived('ModelChanged', 1); });
    await pastTheDebounce();

    expect(stateReads()).toBe(before + 1);
  });
});

// A page the platform declares is drawn exactly as a model's own: one address, the same renderer,
// and its bindings and presses on the platform route the spec names.
describe('a page the platform declares', () => {
  const ACCOUNTS: DashboardSpec = {
    title: 'Accounts',
    sections: [
      {
        widgets: [
          {
            type: 'table',
            title: 'Every account',
            columns: [{ key: 'name', label: 'Account' }],
            rows: { kind: 'service', endpoint: '/api/auth/administration', body: { view: 'accounts' } },
          },
          {
            type: 'action',
            title: 'Model access',
            rows: { kind: 'service', endpoint: '/api/auth/administration', body: { view: 'accounts' } },
            writes: { via: '/api/auth/administration', repeatable: true, choices: [{ label: 'Grant', act: 'grant' }] },
          },
        ],
      },
    ],
  };

  const postsAsking = (view: string) =>
    vi.mocked(apiClient.post).mock.calls.filter(([, body]) => (body as { view: string }).view === view).length;

  beforeEach(() => {
    vi.clearAllMocks();
    seedStore();
    usePlatformPagesStore.setState({ pages: [{ id: 'declared:accounts', name: 'Accounts', routeKey: 'accounts', spec: ACCOUNTS }] });
    vi.mocked(apiClient.post).mockImplementation(async (_path, body) =>
      (body as { view: string }).view === 'accounts' ? [{ id: 'u1', name: 'ada' }] : { said: 'ada may now enter Site A' });
  });

  afterEach(() => { usePlatformPagesStore.setState({ pages: [], loadedFor: null }); });

  it('is drawn at its own address from the rows the platform answers', async () => {
    renderAt('/operations/accounts');

    expect(screen.getByText('Accounts')).toBeInTheDocument();
    expect(await screen.findAllByText('ada')).not.toHaveLength(0);
    expect(vi.mocked(apiClient.post)).toHaveBeenCalledWith('/api/auth/administration', { view: 'accounts' });
  });

  it('reads the page again once a press was taken, so the table shows what the press changed', async () => {
    renderAt('/operations/accounts');
    await screen.findAllByText('ada');
    const before = postsAsking('accounts');

    fireEvent.click(await screen.findByRole('button', { name: 'Grant' }));
    expect(await screen.findByText('ada may now enter Site A')).toBeInTheDocument();

    await waitFor(() => expect(postsAsking('accounts')).toBeGreaterThan(before));
  });
});

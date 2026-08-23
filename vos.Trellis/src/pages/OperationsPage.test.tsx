import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import type { VosThing, VosRelationship } from '../types/vos';

vi.mock('../hooks/useSse', () => ({
  useSse: () => ({ connected: true, on: () => () => {} }),
}));
vi.mock('../api/stateApi', () => ({
  stateApi: { getThingsInState: vi.fn() },
}));

import { stateApi } from '../api/stateApi';
import { useModelStore } from '../stores/modelStore';
import { OperationsPage } from './OperationsPage';

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
    vi.mocked(stateApi.getThingsInState).mockResolvedValue({
      StateName: 'harvested',
      Things: [{ Id: 'p1', Name: 'PLOT-1' }, { Id: 'p2', Name: 'PLOT-2' }],
    });
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

  it('resolves a stateCount funnel bar from the state endpoint', async () => {
    renderAt();
    expect(await screen.findByText('2')).toBeInTheDocument();
    expect(stateApi.getThingsInState).toHaveBeenCalledWith('harvested');
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

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
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
    render(<OperationsPage />);
    expect(screen.getByText('Ops')).toBeInTheDocument();
    expect(screen.getByText('Headline')).toBeInTheDocument();
    expect(screen.getByText('Plots by stage')).toBeInTheDocument();
    expect(screen.getByText('Harvested')).toBeInTheDocument();
  });

  it('offers a scope switcher for the compare archetype', () => {
    render(<OperationsPage />);
    expect(screen.getByText('All sites')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'V-1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'V-2' })).toBeInTheDocument();
  });

  it('resolves a $scope KPI (averaged across sites for "All")', async () => {
    render(<OperationsPage />);
    expect(await screen.findByText('96.5')).toBeInTheDocument(); // (98.9 + 94.1) / 2
  });

  it('re-resolves the KPI when a specific site is selected', async () => {
    render(<OperationsPage />);
    await screen.findByText('96.5');
    fireEvent.click(screen.getByRole('button', { name: 'V-1' }));
    expect(await screen.findByText('98.9')).toBeInTheDocument();
  });

  it('resolves a stateCount funnel bar from the state endpoint', async () => {
    render(<OperationsPage />);
    expect(await screen.findByText('2')).toBeInTheDocument();
    expect(stateApi.getThingsInState).toHaveBeenCalledWith('harvested');
  });

  it('ranks sites in the leaderboard with the winner marked', async () => {
    render(<OperationsPage />);
    expect(await screen.findByText('🏆')).toBeInTheDocument();
    // V-1 / V-2 appear in both the scope switcher and the leaderboard row.
    expect(screen.getAllByText('V-1').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('V-2').length).toBeGreaterThanOrEqual(1);
  });

  it('shows guidance when the model has no Dashboard config', () => {
    useModelStore.setState({ things: [], relationships: [], loaded: true });
    render(<OperationsPage />);
    expect(screen.getByText('No dashboard configured')).toBeInTheDocument();
  });
});

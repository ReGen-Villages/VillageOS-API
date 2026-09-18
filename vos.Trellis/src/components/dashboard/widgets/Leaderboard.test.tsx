import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { Binding, LeaderMetric, LeaderboardWidget } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';

let rows: Row[] = [];

vi.mock('../../../hooks/useDashboard', () => ({
  useBinding: () => ({ loading: false, error: false, value: rows }),
}));

const { Leaderboard } = await import('./Leaderboard');

const ENTITIES: Binding = { kind: 'compareEntities', properties: ['loss'] };

function renderRanking(metric: Omit<LeaderMetric, 'key' | 'label'>): void {
  rows = [
    { id: 'reservoir-a', name: 'Reservoir A', loss: 0 },
    { id: 'reservoir-b', name: 'Reservoir B', loss: 100 },
  ];
  const widget: LeaderboardWidget = {
    type: 'leaderboard',
    entities: ENTITIES,
    metrics: [{ key: 'loss', label: 'Loss', weight: 1, ...metric }],
  };
  render(<Leaderboard widget={widget} ctx={{} as ResolveContext} />);
}

function rankedNames(): string[] {
  return screen.getAllByRole('row').slice(1)
    .map((row) => row.querySelector('td')?.textContent?.replace('🏆', '').trim() ?? '');
}

function scoreBarWidth(name: string): number {
  const row = screen.getByText(name).closest('tr') as HTMLElement;
  return parseFloat((row.querySelector('i') as HTMLElement).style.width);
}

describe('Leaderboard scoring direction', () => {
  it('ranks the lowest value first for a low-is-good metric with stated bounds', () => {
    renderRanking({ direction: 'down-good', best: 0, worst: 100 });

    expect(rankedNames()).toEqual(['Reservoir A', 'Reservoir B']);
    expect(scoreBarWidth('Reservoir A')).toBe(100);
    expect(scoreBarWidth('Reservoir B')).toBe(0);
  });

  it('ranks the highest value first for a high-is-good metric with stated bounds', () => {
    renderRanking({ direction: 'up-good', best: 100, worst: 0 });

    expect(rankedNames()).toEqual(['Reservoir B', 'Reservoir A']);
    expect(scoreBarWidth('Reservoir B')).toBe(100);
    expect(scoreBarWidth('Reservoir A')).toBe(0);
  });

  it('fills in the bounds a low-is-good metric leaves out the low-is-good way round', () => {
    renderRanking({ direction: 'down-good' });

    expect(rankedNames()).toEqual(['Reservoir A', 'Reservoir B']);
    expect(scoreBarWidth('Reservoir A')).toBe(100);
  });

  it('scores a metric stating both bounds the same whichever direction it names', () => {
    renderRanking({ direction: 'up-good', best: 0, worst: 100 });

    expect(rankedNames()).toEqual(['Reservoir A', 'Reservoir B']);
    expect(scoreBarWidth('Reservoir A')).toBe(100);
  });
});

import { describe, it, expect } from 'vitest';
import { breakdownTable } from './breakdownTable';
import type { Row } from '../../../api/dashboardApi';

function keys(rows: Row[], measure: string | null = null): string[] {
  return breakdownTable(rows, measure, 'Name').columns.map((c) => c.key);
}

describe('the columns behind a figure', () => {
  it('leads with the name and follows it with the measure the figure reduced', () => {
    const rows: Row[] = [{ id: '1', name: 'CATCH-1', area: 100, stored: 40 }];
    expect(keys(rows, 'stored')).toEqual(['name', 'stored', 'area']);
  });

  it('never draws the id a row is keyed by', () => {
    expect(keys([{ id: 'abc', name: 'CATCH-1' }])).toEqual(['name']);
  });

  it('puts a property that tells the rows apart ahead of one that reads the same on all of them', () => {
    const rows: Row[] = [
      { id: '1', name: 'A', sameEverywhere: 'gravity fed', area: 10 },
      { id: '2', name: 'B', sameEverywhere: 'gravity fed', area: 20 },
      { id: '3', name: 'C', sameEverywhere: 'gravity fed', area: 30 },
    ];
    expect(keys(rows)).toEqual(['name', 'area', 'sameEverywhere']);
  });

  it('breaks a tie on how many rows carry the property', () => {
    const rows: Row[] = [
      { id: '1', name: 'A', everywhere: 1, rare: 1 },
      { id: '2', name: 'B', everywhere: 2 },
      { id: '3', name: 'C', everywhere: 2 },
    ];
    expect(keys(rows)).toEqual(['name', 'everywhere', 'rare']);
  });

  it('names the properties it had no column for rather than dropping them silently', () => {
    const row: Row = { id: '1', name: 'A' };
    for (const key of ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h']) row[key] = 1;

    const table = breakdownTable([row], null, 'Name');

    expect(table.columns).toHaveLength(7);
    expect(table.omitted).toEqual(['g', 'h']);
  });

  it('treats a column holding any text as text, however many numbers are beside it', () => {
    const rows: Row[] = [
      { id: '1', name: 'A', mixed: 1 },
      { id: '2', name: 'B', mixed: 'unstated' },
    ];
    expect(breakdownTable(rows, null, 'Name').columns.find((c) => c.key === 'mixed')?.numeric).toBe(false);
  });

  it('a column of numbers is numeric', () => {
    const rows: Row[] = [{ id: '1', name: 'A', counted: 4 }];
    expect(breakdownTable(rows, null, 'Name').columns.find((c) => c.key === 'counted')?.numeric).toBe(true);
  });
});

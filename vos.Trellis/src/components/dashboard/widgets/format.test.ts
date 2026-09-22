import { describe, it, expect } from 'vitest';
import type { TableColumn } from '../../../types/dashboard';
import type { Row } from '../../../api/dashboardApi';
import { columnMaxima, deltaTone } from './format';

describe('columnMaxima', () => {
  const ageColumns: TableColumn[] = [
    { key: 'name', label: 'Location' },
    { key: 'age', label: 'Age', numeric: true, render: 'agebar' },
  ];

  it('scales a bar column to its largest value', () => {
    const rows: Row[] = [{ age: 3 }, { age: 11 }, { age: 7 }];

    expect(columnMaxima(rows, ageColumns)).toEqual({ age: 11 });
  });

  it('holds the floor at one, so an all-zero column draws no bar rather than dividing by zero', () => {
    expect(columnMaxima([{ age: 0 }, { age: 0 }], ageColumns)).toEqual({ age: 1 });
  });

  it('measures only the columns drawn as bars', () => {
    expect(columnMaxima([{ name: 'x', age: 2 }], [{ key: 'name', label: 'Location' }])).toEqual({});
  });

  /* A roster no longer needs a row cap, so the list reaching here has no bound. Spreading it over
     Math.max threw once past a hundred thousand rows, taking the whole page render with it. */
  it('measures a list far longer than an argument list could hold', () => {
    const rows: Row[] = Array.from({ length: 200_000 }, (_, i) => ({ age: i }));

    expect(columnMaxima(rows, ageColumns)).toEqual({ age: 199_999 });
  });
});

describe('deltaTone', () => {
  it('colours a rising figure by which way its widget calls good', () => {
    expect(deltaTone(4, 'up-good')).toBe('up');
    expect(deltaTone(4, 'down-good')).toBe('down');
  });

  // A measured area against the one somebody stated is worth noticing whichever way it went, so a
  // widget saying neither way is good gets the neutral tone rather than a verdict on the direction.
  it('leaves a discrepancy uncoloured, whichever way it went', () => {
    expect(deltaTone(60, 'neither-good')).toBe('flat');
    expect(deltaTone(-60, 'neither-good')).toBe('flat');
  });

  it('reads no figure, and no change, as flat', () => {
    expect(deltaTone(null)).toBe('flat');
    expect(deltaTone(0, 'neither-good')).toBe('flat');
  });
});

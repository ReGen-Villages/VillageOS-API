import { describe, expect, it } from 'vitest';
import type { Binding } from '../../../types/dashboard';
import { dayOfYearLabel, groupsByKey, monthNames, monthStarts, windowOf } from './historySeries';

const A_DAY = 86_400;
const A_YEAR = 365 * A_DAY;

function history(windowSeconds: number): Binding {
  return { kind: 'history', property: 'temperature', windowSeconds, steps: [{ fold: 'all', function: 'Max' }] };
}

describe('the window a history binding reads', () => {
  it('reads a year or more in whole years', () => {
    expect(windowOf(history(A_YEAR))).toEqual({ unit: 'years', count: 1 });
    expect(windowOf(history(11 * A_YEAR + 3 * A_DAY))).toEqual({ unit: 'years', count: 11 });
  });

  it('reads less than a year in days', () => {
    expect(windowOf(history(90 * A_DAY))).toEqual({ unit: 'days', count: 90 });
  });

  it('is nothing for a binding that reads no window', () => {
    expect(windowOf({ kind: 'const', value: 1 })).toBeNull();
    expect(windowOf(undefined)).toBeNull();
  });
});

describe('the groups a history binding resolved to', () => {
  it('are read by key, skipping a row that carries no number', () => {
    const groups = groupsByKey([{ key: '1', value: 27.4 }, { key: '2', value: null }, { key: '3', value: 26.1 }]);
    expect([...groups.entries()]).toEqual([['1', 27.4], ['3', 26.1]]);
  });

  it('are none where the binding resolved to nothing or to a scalar', () => {
    expect(groupsByKey(null).size).toBe(0);
    expect(groupsByKey(42).size).toBe(0);
  });
});

describe('month names', () => {
  it('are the reader\'s own, twelve of them, January first', () => {
    const names = monthNames('en');
    expect(names).toHaveLength(12);
    expect(names[0]).toBe('Jan');
    expect(names[11]).toBe('Dec');
    expect(monthNames('de')[2]).toMatch(/^Mär/);
  });
});

describe('a day of the year', () => {
  it('is named as a date without a year, in the reader\'s language', () => {
    expect(dayOfYearLabel(1, 'en')).toMatch(/Jan 1|1 Jan/);
    expect(dayOfYearLabel(366, 'en')).toMatch(/Dec 31|31 Dec/);
    expect(dayOfYearLabel(60, 'de')).toMatch(/29\. Feb/);
  });

  it('knows where each month begins', () => {
    expect(monthStarts()).toEqual([1, 32, 61, 92, 122, 153, 183, 214, 245, 275, 306, 336]);
  });
});

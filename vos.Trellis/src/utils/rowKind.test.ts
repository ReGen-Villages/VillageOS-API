import { describe, it, expect } from 'vitest';
import type { Widget } from '../types/dashboard';
import { rowKindOf, rowsBindingOf } from './rowKind';

const rows = { kind: 'thingList' as const, archetype: 'Spring' };

describe('rowsBindingOf', () => {
  it('finds the rows a table, a timeline, an action list, a leaderboard and a funnel draw from', () => {
    expect(rowsBindingOf({ type: 'table', title: 't', columns: [], rows })).toBe(rows);
    expect(rowsBindingOf({ type: 'gantt', title: 'g', rows })).toBe(rows);
    expect(rowsBindingOf({ type: 'action', title: 'a', rows, writes: { via: '', choices: [] } })).toBe(rows);
    expect(rowsBindingOf({ type: 'leaderboard', title: 'l', entities: rows, metrics: [] })).toBe(rows);
    expect(rowsBindingOf({ type: 'funnel', title: 'f', stages: [{ label: 's', count: { kind: 'const', value: 1 }, drill: rows }] })).toBe(rows);
    expect(rowsBindingOf({ type: 'kpi', title: 'k', value: { kind: 'const', value: 1 } } as Widget)).toBeUndefined();
  });
});

describe('rowKindOf', () => {
  it('is the archetype a list names, the compared kind for the compared Things, and nothing for a service', () => {
    expect(rowKindOf(rows, 'Village')).toBe('Spring');
    expect(rowKindOf({ kind: 'stateList', state: 'dry', archetype: 'Spring' }, undefined)).toBe('Spring');
    expect(rowKindOf({ kind: 'compareEntities', properties: [] }, 'Village')).toBe('Village');
    expect(rowKindOf({ kind: 'service', endpoint: '/api/endpoints/intake' }, 'Village')).toBeUndefined();
    expect(rowKindOf(undefined, 'Village')).toBeUndefined();
  });
});

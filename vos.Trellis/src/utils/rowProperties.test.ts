import { describe, it, expect } from 'vitest';
import type { DashboardSpec, Widget } from '../types/dashboard';
import { keysReadOffARow, readyToKeep } from './rowProperties';

const rows = { kind: 'stateList' as const, state: 'flowing', archetype: 'Spring', computed: [{ key: 'share', value: { kind: 'const' as const, value: 1 } }] };

describe('keysReadOffARow', () => {
  it('collects what a table draws, searches on and sorts by, less the id and the name every row carries', () => {
    const table: Widget = {
      type: 'table', title: 'Springs', rows,
      columns: [{ key: 'name', label: 'Spring' }, { key: 'flow', label: 'Flow' }],
      searchKeys: ['name', 'region'], sortKey: 'flow',
    };
    expect([...keysReadOffARow(table)].sort()).toEqual(['flow', 'region']);
  });

  it('collects what a leaderboard scores and labels with, and what an action list shows beside the name', () => {
    const board: Widget = { type: 'leaderboard', title: 'Springs', entities: rows, metrics: [{ key: 'flow', label: 'Flow' }], labelKey: 'region' };
    expect([...keysReadOffARow(board)].sort()).toEqual(['flow', 'region']);
    const list: Widget = { type: 'action', title: 'Springs', rows, label: 'code', shows: ['flow'], writes: { via: 'intake', choices: [] }, asks: [{ key: 'to', label: 'To', shows: ['capacity'] }] };
    expect([...keysReadOffARow(list)].sort()).toEqual(['capacity', 'code', 'flow']);
  });
});

describe('readyToKeep', () => {
  it('names under a state list the keys its widget reads, less the columns worked out per row, and marks the page designed', () => {
    const spec: DashboardSpec = {
      title: 'Springs',
      sections: [{ layout: 'grid', widgets: [{ type: 'table', title: 'Springs', rows, columns: [{ key: 'flow', label: 'Flow' }, { key: 'share', label: 'Share' }] }] }],
    };
    const kept = readyToKeep(spec);
    expect(kept.designed).toBe(true);
    expect((kept.sections[0].widgets[0] as { rows: { properties?: string[] } }).rows.properties).toEqual(['flow']);
  });

  it('takes the list away where the widget reads nothing beyond the name, and leaves a binding under computed alone', () => {
    const nested = { ...rows, computed: [{ key: 'count', value: { kind: 'stateList' as const, state: 'full', archetype: 'Reservoir', properties: ['level'] } }] };
    const spec: DashboardSpec = {
      title: 'Springs',
      sections: [{ layout: 'grid', widgets: [{ type: 'table', title: 'Springs', rows: { ...nested, properties: ['stale'] }, columns: [{ key: 'name', label: 'Spring' }] }] }],
    };
    const held = (readyToKeep(spec).sections[0].widgets[0] as { rows: typeof nested & { properties?: string[] } }).rows;
    expect('properties' in held).toBe(false);
    expect((held.computed[0].value as { properties?: string[] }).properties).toEqual(['level']);
  });
});

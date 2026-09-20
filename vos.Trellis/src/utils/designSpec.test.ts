import { describe, it, expect } from 'vitest';
import { GRID_COLUMNS, type DashboardSpec, type Widget } from '../types/dashboard';
import { WIDGET_KINDS } from './gridLayout';
import { emptyWidget, isKeptPage, newPage, nextPlacement, openedForDesign, unboundSlots } from './designSpec';

const figure = (title: string): Widget => ({ type: 'kpi', title, value: { kind: 'const', value: 1 } });

describe('newPage', () => {
  it('starts one empty grid section, marked as designed, with an icon the sidebar can draw', () => {
    const page = newPage('Reservoirs');
    expect(page.title).toBe('Reservoirs');
    expect(page.designed).toBe(true);
    expect(page.icon).toBeTruthy();
    expect(page.sections).toEqual([{ layout: 'grid', widgets: [] }]);
  });
});

describe('openedForDesign', () => {
  const seeded: DashboardSpec = {
    title: 'Springs',
    sections: [
      { title: 'Flow', widgets: [figure('a'), figure('b')] },
      { layout: 'split', widths: [2, 1], widgets: [figure('c'), figure('d')] },
    ],
  };

  it('turns every section into a grid with a placement per widget and drops the split widths', () => {
    const opened = openedForDesign(seeded);
    expect(opened.designed).toBe(true);
    for (const section of opened.sections) {
      expect(section.layout).toBe('grid');
      expect('widths' in section).toBe(false);
      for (const widget of section.widgets) expect(widget.placement).toBeDefined();
    }
    expect(opened.sections[1].widgets.map((widget) => widget.placement?.width)).toEqual([8, 4]);
  });

  it('keeps everything else the page says, and leaves the page it was read from untouched', () => {
    const opened = openedForDesign(seeded);
    expect(opened.title).toBe('Springs');
    expect(opened.sections[0].title).toBe('Flow');
    expect(seeded.sections[0].layout).toBeUndefined();
    expect(seeded.sections[0].widgets[0].placement).toBeUndefined();
  });

  it('drops a composition, since a table edited as a table can no longer be re-derived', () => {
    const composed: DashboardSpec = { ...seeded, composed: { kind: 'Reservoir', columns: [] } };
    expect('composed' in openedForDesign(composed)).toBe(false);
  });

  it('opens a grid page as it stands', () => {
    const designed = openedForDesign(seeded);
    expect(openedForDesign(designed)).toEqual(designed);
  });
});

describe('emptyWidget', () => {
  it('builds a widget of every kind the palette offers, titled and drawn by the renderer', () => {
    for (const kind of WIDGET_KINDS) {
      const widget = emptyWidget(kind, 'Untitled');
      expect(widget.type).toBe(kind);
      expect(widget.title).toBe('Untitled');
    }
  });
});

describe('unboundSlots', () => {
  it('names the binding slots the contract requires and the widget has not been given', () => {
    expect(unboundSlots(emptyWidget('kpi', 't'))).toEqual(['value']);
    expect(unboundSlots(emptyWidget('table', 't'))).toEqual(['rows']);
    expect(unboundSlots(emptyWidget('leaderboard', 't'))).toEqual(['entities']);
    expect(unboundSlots(figure('bound'))).toEqual([]);
  });
});

describe('nextPlacement', () => {
  it('lands under everything the section holds, against the left side, no wider than the grid', () => {
    const section = { layout: 'grid' as const, widgets: [{ ...figure('a'), placement: { column: 3, row: 2, width: 3, height: 4 } }] };
    expect(nextPlacement(section, { width: 20, height: 2 })).toEqual({ column: 0, row: 6, width: GRID_COLUMNS, height: 2 });
    expect(nextPlacement({ layout: 'grid', widgets: [] }, { width: 3, height: 3 })).toEqual({ column: 0, row: 0, width: 3, height: 3 });
  });
});

describe('isKeptPage', () => {
  it('is true for a composed or a designed page, and false for a seeded one', () => {
    expect(isKeptPage({ title: 's', sections: [] })).toBe(false);
    expect(isKeptPage({ title: 'c', sections: [], composed: { kind: 'Site', columns: [] } })).toBe(true);
    expect(isKeptPage({ title: 'd', sections: [], designed: true })).toBe(true);
  });
});

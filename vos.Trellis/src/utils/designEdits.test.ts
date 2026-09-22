import { describe, it, expect } from 'vitest';
import type { DashboardSpecification, Widget } from '../types/dashboard';
import {
  layoutItemsOf,
  placementsFromLayout,
  withPageWritten,
  withPlacements,
  withSectionAdded,
  withSectionMoved,
  withSectionRemoved,
  withSectionWritten,
  withWidgetAdded,
  withWidgetMoved,
  withWidgetRemoved,
  withWidgetReplaced,
} from './designEdits';

const figure = (title: string): Widget => ({ type: 'kpi', title, value: { kind: 'const', value: 1 } });
const placed = (title: string, column: number, row: number): Widget => ({ ...figure(title), placement: { column, row, width: 3, height: 3 } });

const page = (): DashboardSpecification => ({
  title: 'Springs',
  designed: true,
  sections: [
    { title: 'Flow', layout: 'grid', widgets: [placed('a', 0, 0), placed('b', 3, 0)] },
    { title: 'Quality', layout: 'grid', widgets: [placed('c', 0, 0)] },
  ],
});

describe('widgets', () => {
  it('adds a widget at the placement given, replaces one, and removes one', () => {
    const added = withWidgetAdded(page(), 1, figure('d'), { column: 6, row: 0, width: 3, height: 3 });
    expect(added.sections[1].widgets.map((widget) => widget.title)).toEqual(['c', 'd']);
    expect(added.sections[1].widgets[1].placement).toEqual({ column: 6, row: 0, width: 3, height: 3 });

    const replaced = withWidgetReplaced(added, 1, 1, figure('e'));
    expect(replaced.sections[1].widgets[1].title).toBe('e');

    const removed = withWidgetRemoved(replaced, 0, 0);
    expect(removed.sections[0].widgets.map((widget) => widget.title)).toEqual(['b']);
  });

  it('moves a widget to another section at the placement given', () => {
    const moved = withWidgetMoved(page(), { section: 0, widget: 1 }, 1, { column: 3, row: 0, width: 3, height: 3 });
    expect(moved.sections[0].widgets.map((widget) => widget.title)).toEqual(['a']);
    expect(moved.sections[1].widgets.map((widget) => widget.title)).toEqual(['c', 'b']);
    expect(moved.sections[1].widgets[1].placement).toEqual({ column: 3, row: 0, width: 3, height: 3 });
  });
});

describe('withPlacements', () => {
  it('answers the same specification when nothing moved, so the grid does not report it round', () => {
    const held = page();
    const same = withPlacements(held, 0, [{ column: 0, row: 0, width: 3, height: 3 }, { column: 3, row: 0, width: 3, height: 3 }]);
    expect(same).toBe(held);
  });

  it('writes the placements that moved and keeps one the grid did not report', () => {
    const next = withPlacements(page(), 0, [{ column: 0, row: 3, width: 6, height: 3 }]);
    expect(next.sections[0].widgets[0].placement).toEqual({ column: 0, row: 3, width: 6, height: 3 });
    expect(next.sections[0].widgets[1].placement).toEqual({ column: 3, row: 0, width: 3, height: 3 });
  });
});

describe('sections', () => {
  it('adds an empty grid section at the end, removes one, and moves one earlier or later within bounds', () => {
    const added = withSectionAdded(page());
    expect(added.sections).toHaveLength(3);
    expect(added.sections[2]).toEqual({ layout: 'grid', widgets: [] });

    const removed = withSectionRemoved(added, 0);
    expect(removed.sections.map((section) => section.title)).toEqual(['Quality', undefined]);

    const later = withSectionMoved(page(), 0, 'later');
    expect(later.sections.map((section) => section.title)).toEqual(['Quality', 'Flow']);
    const held = page();
    expect(withSectionMoved(held, 0, 'earlier')).toBe(held);
  });

  it('writes a title and a hint, and takes a key away when its value is emptied', () => {
    const written = withSectionWritten(page(), 1, { title: 'Purity', hint: 'sampled daily' });
    expect(written.sections[1]).toMatchObject({ title: 'Purity', hint: 'sampled daily' });
    const cleared = withSectionWritten(written, 1, { hint: '' });
    expect('hint' in cleared.sections[1]).toBe(false);
  });
});

describe('withPageWritten', () => {
  it('writes the page fields and drops one written empty or zero', () => {
    const written = withPageWritten(page(), { subtitle: 'by spring', refreshSeconds: 30 });
    expect(written).toMatchObject({ subtitle: 'by spring', refreshSeconds: 30 });
    const cleared = withPageWritten(written, { refreshSeconds: 0 });
    expect('refreshSeconds' in cleared).toBe(false);
  });
});

describe('the grid items', () => {
  it('stand for the section widgets by position and come back in the section order', () => {
    const items = layoutItemsOf(page().sections[0]);
    expect(items).toEqual([{ i: '0', x: 0, y: 0, w: 3, h: 3 }, { i: '1', x: 3, y: 0, w: 3, h: 3 }]);
    const placements = placementsFromLayout([items[1], { ...items[0], y: 6 }], 2);
    expect(placements[0]).toEqual({ column: 0, row: 6, width: 3, height: 3 });
    expect(placements[1]).toEqual({ column: 3, row: 0, width: 3, height: 3 });
  });
});

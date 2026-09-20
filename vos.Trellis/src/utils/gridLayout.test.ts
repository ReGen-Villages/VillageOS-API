import { describe, it, expect } from 'vitest';
import { GRID_COLUMNS, type DashboardSection, type Widget } from '../types/dashboard';
import {
  DEFAULT_SIZE,
  GRID_ROW_HEIGHT,
  WIDGET_KINDS,
  defaultSizeOf,
  layoutOf,
  placementsOf,
  readingOrder,
  sectionGrid,
  splitWidths,
} from './gridLayout';

const figure = (title: string): Widget => ({ type: 'kpi', title, value: { kind: 'const', value: 1 } });
const table = (title: string, visibleRows?: number): Widget =>
  ({ type: 'table', title, columns: [], rows: { kind: 'const', value: 0 }, visibleRows }) as Widget;

describe('layoutOf', () => {
  it('reads a section of figures stating no layout as a strip, and any other as full width', () => {
    expect(layoutOf({ widgets: [figure('a'), figure('b')] })).toBe('kpi-strip');
    expect(layoutOf({ widgets: [figure('a'), table('t')] })).toBe('single');
    expect(layoutOf({ layout: 'grid', widgets: [] })).toBe('grid');
  });
});

describe('splitWidths', () => {
  it('scales stated widths to the grid so they add up to it exactly', () => {
    expect(splitWidths([2, 1])).toEqual([8, 4]);
    expect(splitWidths([1, 1, 1])).toEqual([4, 4, 4]);
    expect(splitWidths([1, 1, 1, 1, 1]).reduce((sum, width) => sum + width, 0)).toBe(GRID_COLUMNS);
  });

  it('keeps at least one column for every widget however narrow its share', () => {
    const widths = splitWidths([100, 1]);
    expect(widths[1]).toBe(1);
    expect(widths.reduce((sum, width) => sum + width, 0)).toBe(GRID_COLUMNS);
  });
});

describe('placementsOf', () => {
  it('lays a strip four across and wraps the fifth figure onto a second row', () => {
    const section: DashboardSection = { widgets: ['a', 'b', 'c', 'd', 'e'].map(figure) };
    const placements = placementsOf(section);
    expect(placements.map((placement) => [placement.column, placement.row])).toEqual([[0, 0], [3, 0], [6, 0], [9, 0], [0, DEFAULT_SIZE.kpi.height]]);
    expect(placements.every((placement) => placement.width === 3)).toBe(true);
  });

  it('lays a split in its stated widths on one row', () => {
    const section: DashboardSection = { layout: 'split', widths: [2, 1], widgets: [table('left'), table('right')] };
    expect(placementsOf(section).map((placement) => [placement.column, placement.width, placement.row])).toEqual([[0, 8, 0], [8, 4, 0]]);
  });

  it('stacks a full-width section, each widget under the one before', () => {
    const section: DashboardSection = { layout: 'single', widgets: [table('a', 4), figure('b')] };
    const [first, second] = placementsOf(section);
    expect(first).toMatchObject({ column: 0, row: 0, width: GRID_COLUMNS });
    expect(second).toMatchObject({ column: 0, row: first.height, width: GRID_COLUMNS });
  });

  it('keeps what a grid widget carries and stacks one carrying nothing under the rest', () => {
    const placed: Widget = { ...figure('a'), placement: { column: 3, row: 2, width: 3, height: 3 } };
    const section: DashboardSection = { layout: 'grid', widgets: [placed, figure('b')] };
    const [first, second] = placementsOf(section);
    expect(first).toEqual({ column: 3, row: 2, width: 3, height: 3 });
    expect(second).toMatchObject({ column: 0, row: 5, width: GRID_COLUMNS });
  });
});

describe('defaultSizeOf', () => {
  it('names a size for every widget kind the renderer draws', () => {
    expect(WIDGET_KINDS.length).toBeGreaterThan(0);
    for (const kind of WIDGET_KINDS) {
      expect(DEFAULT_SIZE[kind].width).toBeGreaterThan(0);
      expect(DEFAULT_SIZE[kind].width).toBeLessThanOrEqual(GRID_COLUMNS);
      expect(DEFAULT_SIZE[kind].height).toBeGreaterThan(0);
    }
  });

  it('sizes a table to the rows it caps itself at, and a figure with a trace taller than one without', () => {
    expect(defaultSizeOf(table('short', 2)).height).toBeLessThan(defaultSizeOf(table('long', 20)).height);
    const plain = figure('plain');
    const traced: Widget = { ...plain, spark: { kind: 'const', value: 0 } } as Widget;
    expect(defaultSizeOf(traced).height).toBeGreaterThan(defaultSizeOf(plain).height);
  });
});

describe('readingOrder', () => {
  it('reads top to bottom, then left to right', () => {
    const order = readingOrder([
      { column: 6, row: 0, width: 6, height: 2 },
      { column: 0, row: 2, width: 12, height: 2 },
      { column: 0, row: 0, width: 6, height: 2 },
    ]);
    expect(order).toEqual([2, 0, 1]);
  });
});

describe('sectionGrid', () => {
  it('draws a grid section with a column span and a row span per widget on one row unit', () => {
    const section: DashboardSection = {
      layout: 'grid',
      widgets: [{ ...figure('a'), placement: { column: 3, row: 1, width: 3, height: 2 } }],
    };
    const grid = sectionGrid(section, true);
    expect(grid.gridTemplateColumns).toBe(`repeat(${GRID_COLUMNS}, minmax(0, 1fr))`);
    expect(grid.gridAutoRows).toBe(`${GRID_ROW_HEIGHT}px`);
    expect(grid.cells[0]).toEqual({ gridColumn: '4 / span 3', gridRow: '2 / span 2' });
  });

  it('stacks a grid section in reading order when the screen is narrow', () => {
    const section: DashboardSection = {
      layout: 'grid',
      widgets: [
        { ...figure('right'), placement: { column: 6, row: 0, width: 6, height: 2 } },
        { ...figure('left'), placement: { column: 0, row: 0, width: 6, height: 2 } },
      ],
    };
    const grid = sectionGrid(section, false);
    expect(grid.gridTemplateColumns).toBe('minmax(0, 1fr)');
    expect(grid.cells).toEqual([{ order: 1 }, { order: 0 }]);
  });

  it('draws the three fixed layouts as before', () => {
    expect(sectionGrid({ widgets: [figure('a'), figure('b')] }, true).gridTemplateColumns).toBe('repeat(2, minmax(0, 1fr))');
    expect(sectionGrid({ layout: 'split', widths: [2, 1], widgets: [table('a'), table('b')] }, true).gridTemplateColumns).toBe('minmax(0, 2fr) minmax(0, 1fr)');
    expect(sectionGrid({ layout: 'single', widgets: [table('a')] }, true).gridTemplateColumns).toBe('minmax(0, 1fr)');
    expect(sectionGrid({ layout: 'split', widths: [2, 1], widgets: [table('a'), table('b')] }, false).gridTemplateColumns).toBe('minmax(0, 1fr)');
  });
});

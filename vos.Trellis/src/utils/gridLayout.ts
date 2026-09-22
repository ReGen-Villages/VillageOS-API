import type { CSSProperties } from 'react';
import { GRID_COLUMNS, type DashboardSection, type Placement, type Widget } from '../types/dashboard';

/**
 * Where each widget of a section stands.
 *
 * A `grid` section places each widget where its own placement says. The three fixed layouts — a
 * strip of figures, a weighted split, full width — place theirs by rule, and the rules are kept here
 * in both directions: what the operations page draws for each, and the placements a section converts
 * to when the Design page opens it, so a seeded page opens exactly as it is drawn.
 */

/** One row of a grid section, in pixels. */
export const GRID_ROW_HEIGHT = 40;

/** The gap between cells, in pixels — the `gap-3.5` every section is drawn with. */
export const GRID_GAP = 14;

/** The most figures a strip draws across before wrapping. */
const STRIP_COLUMNS = 4;

/** What a widget of each kind takes up when it is dropped from the palette, or when a fixed layout
 *  converts to placements: as much as it needs to be read, until somebody resizes it. Typed over the
 *  union, so a kind added to the contract without a size here fails the build. */
export const DEFAULT_SIZE: Record<Widget['type'], { width: number; height: number }> = {
  kpi: { width: 3, height: 3 },
  funnel: { width: 6, height: 8 },
  bullet: { width: 6, height: 6 },
  table: { width: 6, height: 8 },
  gantt: { width: 12, height: 7 },
  leaderboard: { width: 6, height: 7 },
  verdict: { width: 6, height: 5 },
  working: { width: 6, height: 5 },
  exceptionBar: { width: 12, height: 3 },
  rangeBar: { width: 6, height: 7 },
  lineSeries: { width: 6, height: 7 },
  heatmap: { width: 6, height: 7 },
  stackedShares: { width: 6, height: 4 },
  divergingBar: { width: 6, height: 5 },
  smallMultiples: { width: 6, height: 6 },
  action: { width: 6, height: 8 },
  form: { width: 4, height: 8 },
};

/** Every kind of widget the renderer draws, which is what the palette offers. */
export const WIDGET_KINDS = Object.keys(DEFAULT_SIZE) as Widget['type'][];

/** A figure drawing a trace is taller than one drawing a number. */
const SPARK_ROWS = 2;

/** The rows a table shows where it states no cap. */
const TABLE_ROWS_ASSUMED = 8;

/** The card around a table's rows — its padding, title and search box — and one body row, both in
 *  pixels, so a table's cell is sized to the rows it caps itself at. */
const TABLE_CHROME_PIXELS = 120;
const TABLE_ROW_PIXELS = 36;

/** The same for a form: its card, title and the row its press stands on, then one labelled field
 *  with the gap under it. A field picking many from a roster is a box of them, several fields tall. */
const FORM_CHROME_PIXELS = 110;
const FORM_FIELD_PIXELS = 50;
const MULTICHOICE_FIELDS = 3;

function rowsFor(pixels: number): number {
  return Math.max(1, Math.ceil((pixels + GRID_GAP) / (GRID_ROW_HEIGHT + GRID_GAP)));
}

export function defaultSizeOf(widget: Widget): { width: number; height: number } {
  const size = DEFAULT_SIZE[widget.type];
  if (widget.type === 'kpi' && widget.spark) return { ...size, height: size.height + SPARK_ROWS };
  if (widget.type === 'table') {
    const rows = widget.visibleRows ?? TABLE_ROWS_ASSUMED;
    return { ...size, height: rowsFor(TABLE_CHROME_PIXELS + rows * TABLE_ROW_PIXELS) };
  }
  if (widget.type === 'form') {
    const fields = widget.fields.reduce((sum, field) => sum + (field.kind === 'multichoice' ? MULTICHOICE_FIELDS : 1), 0);
    return { ...size, height: rowsFor(FORM_CHROME_PIXELS + fields * FORM_FIELD_PIXELS) };
  }
  return size;
}

/** The layout a section draws with. A section stating none is a strip when every widget is a
 *  figure and full width otherwise, which is how every page has always been read. */
export function layoutOf(section: DashboardSection): NonNullable<DashboardSection['layout']> {
  return section.layout ?? (section.widgets.every((widget) => widget.type === 'kpi') ? 'kpi-strip' : 'single');
}

/** Stated widths scaled to the grid's columns so they add up to it exactly; every widget keeps at
 *  least one column, and what rounding leaves over goes to the widest fractions first. */
export function splitWidths(widths: number[]): number[] {
  if (widths.length === 0) return [];
  const total = widths.reduce((sum, width) => sum + width, 0);
  const exact = widths.map((width) => (total > 0 ? (width / total) * GRID_COLUMNS : GRID_COLUMNS / widths.length));
  const scaled = exact.map((share) => Math.max(1, Math.floor(share)));
  const byFraction = exact
    .map((share, index) => ({ index, fraction: share - Math.floor(share) }))
    .sort((a, b) => b.fraction - a.fraction || a.index - b.index);
  let left = GRID_COLUMNS - scaled.reduce((sum, width) => sum + width, 0);
  for (const { index } of byFraction) {
    if (left <= 0) break;
    scaled[index] += 1;
    left -= 1;
  }
  while (left < 0) {
    const widest = scaled.indexOf(Math.max(...scaled));
    scaled[widest] -= 1;
    left += 1;
  }
  return scaled;
}

/** Indexes of the placements in reading order: top to bottom, then left to right. */
export function readingOrder(placements: Placement[]): number[] {
  return placements
    .map((placement, index) => ({ placement, index }))
    .sort((a, b) => a.placement.row - b.placement.row || a.placement.column - b.placement.column || a.index - b.index)
    .map(({ index }) => index);
}

function bottomOf(placements: Placement[]): number {
  return placements.reduce((bottom, placement) => Math.max(bottom, placement.row + placement.height), 0);
}

/** Where each widget of a section stands, whatever layout the section states. A fixed layout is
 *  converted without loss: a strip four across, a split in its stated widths, a full-width section
 *  stacked; a grid widget keeps what it carries, and one carrying nothing is stacked under the rest. */
export function placementsOf(section: DashboardSection): Placement[] {
  const layout = layoutOf(section);
  const sizes = section.widgets.map(defaultSizeOf);
  if (layout === 'kpi-strip') {
    const across = Math.min(section.widgets.length, STRIP_COLUMNS);
    const width = GRID_COLUMNS / across;
    return sizes.map((size, index) => ({
      column: (index % across) * width,
      row: Math.floor(index / across) * size.height,
      width,
      height: size.height,
    }));
  }
  if (layout === 'split') {
    const widths = splitWidths(section.widths ?? section.widgets.map(() => 1));
    let column = 0;
    return sizes.map((size, index) => {
      const placement = { column, row: 0, width: widths[index], height: size.height };
      column += widths[index];
      return placement;
    });
  }
  const placed: Placement[] = [];
  section.widgets.forEach((widget, index) => {
    const own = layout === 'grid' ? widget.placement : undefined;
    placed.push(own ?? { column: 0, row: bottomOf(placed), width: GRID_COLUMNS, height: sizes[index].height });
  });
  return placed;
}

export interface SectionGrid {
  gridTemplateColumns: string;
  gridAutoRows?: string;
  /** One style per widget, in the section's own order. */
  cells: CSSProperties[];
}

/** What the operations page draws a section with: the columns, the row unit where the section is a
 *  grid, and a style per cell. Every track states a zero minimum — a bare `1fr` grows to its widest
 *  content, and one long unbreakable cell would then widen the page rather than scroll inside its
 *  card. Narrow, every layout is one column, and a grid keeps its reading order. */
export function sectionGrid(section: DashboardSection, wide: boolean): SectionGrid {
  const layout = layoutOf(section);
  const none = section.widgets.map(() => ({}));
  if (!wide) {
    if (layout !== 'grid') return { gridTemplateColumns: 'minmax(0, 1fr)', cells: none };
    const cells: CSSProperties[] = section.widgets.map(() => ({}));
    readingOrder(placementsOf(section)).forEach((widgetIndex, position) => { cells[widgetIndex] = { order: position }; });
    return { gridTemplateColumns: 'minmax(0, 1fr)', cells };
  }
  switch (layout) {
    case 'grid':
      return {
        gridTemplateColumns: `repeat(${GRID_COLUMNS}, minmax(0, 1fr))`,
        gridAutoRows: `${GRID_ROW_HEIGHT}px`,
        cells: placementsOf(section).map((placement) => ({
          gridColumn: `${placement.column + 1} / span ${placement.width}`,
          gridRow: `${placement.row + 1} / span ${placement.height}`,
        })),
      };
    case 'kpi-strip':
      return { gridTemplateColumns: `repeat(${Math.min(section.widgets.length, STRIP_COLUMNS)}, minmax(0, 1fr))`, cells: none };
    case 'split': {
      const widths = section.widths ?? section.widgets.map(() => 1);
      return { gridTemplateColumns: widths.map((width) => `minmax(0, ${width}fr)`).join(' '), cells: none };
    }
    case 'single':
      return { gridTemplateColumns: 'minmax(0, 1fr)', cells: none };
  }
}

import { GRID_COLUMNS, type DashboardSection, type DashboardSpec, type Placement, type Widget } from '../types/dashboard';
import { placementsOf } from './gridLayout';

/**
 * A page as the Design page holds it: every section a grid, every widget placed.
 *
 * Nothing here is a second kind of page. A page the designer opens is the specification the console
 * draws with its three fixed layouts converted to placements; a page it starts is one empty grid; a
 * widget it drops is the kind alone, titled and waiting for its binding.
 */

/** The icon a page starts with, from the set the sidebar draws with. */
const NEW_PAGE_ICON = 'layout-template';

export function newPage(name: string): DashboardSpec {
  return { title: name, icon: NEW_PAGE_ICON, sections: [{ layout: 'grid', widgets: [] }], designed: true };
}

/** A page the console kept — composed or designed — as against one the seed published. */
export function isKeptPage(spec: DashboardSpec): boolean {
  return !!spec.composed || !!spec.designed;
}

/** The specification with every section as a grid, marked as the designer's, and a composition
 *  dropped — once its table is edited as a table the composer could no longer re-derive the choices. */
export function openedForDesign(spec: DashboardSpec): DashboardSpec {
  const { composed: _composed, ...rest } = spec;
  return { ...rest, sections: spec.sections.map(asGrid), designed: true };
}

function asGrid(section: DashboardSection): DashboardSection {
  const { widths: _widths, ...rest } = section;
  const placements = placementsOf(section);
  return {
    ...rest,
    layout: 'grid',
    widgets: section.widgets.map((widget, index) => ({ ...widget, placement: placements[index] })),
  };
}

/** A binding the contract requires but the designer has not been handed yet. The panel beside the
 *  canvas fills it; until then the widget draws a placeholder naming what it waits for, which is what
 *  {@link unboundSlots} reads. */
const UNBOUND = undefined as unknown as never;

export function emptyWidget(kind: Widget['type'], title: string): Widget {
  switch (kind) {
    case 'kpi': return { type: 'kpi', title, value: UNBOUND };
    case 'funnel': return { type: 'funnel', title, stages: [] };
    case 'bullet': return { type: 'bullet', title, rows: [] };
    case 'table': return { type: 'table', title, columns: [], rows: UNBOUND };
    case 'gantt': return { type: 'gantt', title, rows: UNBOUND };
    case 'leaderboard': return { type: 'leaderboard', title, entities: UNBOUND, metrics: [] };
    case 'verdict': return { type: 'verdict', title, rows: [] };
    case 'working': return { type: 'working', title, rows: [] };
    case 'exceptionBar': return { type: 'exceptionBar', title, buckets: [] };
    case 'rangeBar': return { type: 'rangeBar', title, months: UNBOUND };
    case 'lineSeries': return { type: 'lineSeries', title, series: [] };
    case 'heatmap': return { type: 'heatmap', title, value: UNBOUND };
    case 'stackedShares': return { type: 'stackedShares', title, classes: [] };
    case 'divergingBar': return { type: 'divergingBar', title, up: UNBOUND, down: UNBOUND };
    case 'smallMultiples': return { type: 'smallMultiples', title, bars: UNBOUND, line: UNBOUND };
    case 'action': return { type: 'action', title, rows: UNBOUND, writes: { via: '', choices: [] } };
    case 'form': return { type: 'form', title, fields: [], submit: '', writes: { via: '', act: '' } };
  }
}

/** The binding slots the contract requires that this widget has not been given. */
export function unboundSlots(widget: Widget): string[] {
  switch (widget.type) {
    case 'kpi':
    case 'heatmap':
      return widget.value ? [] : ['value'];
    case 'table':
    case 'gantt':
    case 'action':
      return widget.rows ? [] : ['rows'];
    case 'leaderboard': return widget.entities ? [] : ['entities'];
    case 'rangeBar': return widget.months ? [] : ['months'];
    case 'divergingBar': return [widget.up ? null : 'up', widget.down ? null : 'down'].filter((slot): slot is string => slot !== null);
    case 'smallMultiples': return [widget.bars ? null : 'bars', widget.line ? null : 'line'].filter((slot): slot is string => slot !== null);
    default: return [];
  }
}

/** Where a widget of this size lands when it is added without being dropped: under everything the
 *  section holds, against the left side. */
export function nextPlacement(section: DashboardSection, size: { width: number; height: number }): Placement {
  const bottom = section.widgets.reduce(
    (deepest, widget) => Math.max(deepest, (widget.placement?.row ?? 0) + (widget.placement?.height ?? 0)),
    0,
  );
  return { column: 0, row: bottom, width: Math.min(size.width, GRID_COLUMNS), height: size.height };
}

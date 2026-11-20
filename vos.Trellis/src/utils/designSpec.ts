import { GRID_COLUMNS, type DashboardSection, type DashboardSpecification, type Placement, type Widget } from '@tendril-ui/tendril';
import { placementsOf } from './gridLayout';

/** Not a second kind of page: an opened page is the console's specification with its fixed layouts turned
 *  into placements, a new page is one empty grid, and a dropped widget is its kind alone, waiting for its binding. */

/** The icon a page starts with, from the set the sidebar draws with. */
const NEW_PAGE_ICON = 'layout-template';

export function newPage(name: string): DashboardSpecification {
  return { title: name, icon: NEW_PAGE_ICON, sections: [{ layout: 'grid', widgets: [] }], designed: true };
}

/** A page the console kept — composed or designed — as against one the seed published. */
export function isKeptPage(specification: DashboardSpecification): boolean {
  return !!specification.composed || !!specification.designed;
}

/** The composition is dropped, because once its table is edited the composer could not work back to the choices. */
export function openedForDesign(specification: DashboardSpecification): DashboardSpecification {
  const { composed: _composed, ...rest } = specification;
  return { ...rest, sections: specification.sections.map(asGrid), designed: true };
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
    case 'boxPlot': return { type: 'boxPlot', title, months: UNBOUND };
    case 'depthProfile': return { type: 'depthProfile', title, rows: UNBOUND, labelKey: '', parts: [] };
    case 'polarShares': return {
      type: 'polarShares', title, counts: UNBOUND, cells: UNBOUND, bearingKey: '', classOrderKey: '', classLabelKey: '', classColourKey: '',
    };
    case 'densityGrid': return { type: 'densityGrid', title, cells: UNBOUND, across: { label: '' }, up: { label: '' } };
    case 'lineSeries': return { type: 'lineSeries', title, series: [] };
    case 'heatmap': return { type: 'heatmap', title, value: UNBOUND };
    case 'stackedShares': return { type: 'stackedShares', title, classes: [] };
    case 'divergingBar': return { type: 'divergingBar', title, up: UNBOUND, down: UNBOUND };
    case 'smallMultiples': return { type: 'smallMultiples', title, bars: UNBOUND, line: UNBOUND };
    case 'action': return { type: 'action', title, rows: UNBOUND, writes: { via: '', choices: [] } };
    case 'form': return { type: 'form', title, fields: [], submit: '', writes: { via: '', act: '' } };
  }
}

export function unboundSlots(widget: Widget): string[] {
  switch (widget.type) {
    case 'kpi':
    case 'heatmap':
      return widget.value ? [] : ['value'];
    case 'table':
    case 'gantt':
    case 'action':
    case 'depthProfile':
      return widget.rows ? [] : ['rows'];
    case 'leaderboard': return widget.entities ? [] : ['entities'];
    case 'densityGrid': return widget.cells ? [] : ['cells'];
    case 'polarShares': return [widget.counts ? null : 'counts', widget.cells ? null : 'cells'].filter((slot): slot is string => slot !== null);
    case 'rangeBar':
    case 'boxPlot':
      return widget.months ? [] : ['months'];
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

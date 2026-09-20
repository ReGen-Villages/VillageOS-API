import type { DashboardSection, DashboardSpec, Placement, Widget } from '../types/dashboard';

/**
 * Every edit the Design page makes, as a function over the specification.
 *
 * The canvas and the panels call these and nothing else, so each decision — where a moved widget
 * lands, what an empty field does to its key, where a new section goes — can be tried without a
 * canvas. Nothing here writes to the model; keeping the page is the writes' job.
 */

export type DesignSelection =
  | { on: 'page' }
  | { on: 'translations' }
  | { on: 'section'; section: number }
  | { on: 'widget'; section: number; widget: number };

/** One item of the grid the canvas draws a section with: its position in the section as the id. */
export interface GridItem {
  i: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

function withSection(spec: DashboardSpec, index: number, next: DashboardSection): DashboardSpec {
  return { ...spec, sections: spec.sections.map((section, at) => (at === index ? next : section)) };
}

function withWidgets(spec: DashboardSpec, index: number, widgets: Widget[]): DashboardSpec {
  return withSection(spec, index, { ...spec.sections[index], widgets });
}

export function withWidgetAdded(spec: DashboardSpec, section: number, widget: Widget, placement: Placement): DashboardSpec {
  return withWidgets(spec, section, [...spec.sections[section].widgets, { ...widget, placement }]);
}

/** The grid reports its layout on every change and on none, so placements it did not move leave the
 *  specification as it was — a fresh specification for an unchanged layout would be drawn, reported,
 *  and written again without end. */
export function withPlacements(spec: DashboardSpec, section: number, placements: Placement[]): DashboardSpec {
  const held = spec.sections[section].widgets;
  const same = (a: Placement | undefined, b: Placement | undefined) =>
    a === b || (!!a && !!b && a.column === b.column && a.row === b.row && a.width === b.width && a.height === b.height);
  if (held.every((widget, index) => same(widget.placement, placements[index] ?? widget.placement))) return spec;
  return withWidgets(spec, section, held.map((widget, index) => ({ ...widget, placement: placements[index] ?? widget.placement })));
}

export function withWidgetRemoved(spec: DashboardSpec, section: number, widget: number): DashboardSpec {
  return withWidgets(spec, section, spec.sections[section].widgets.filter((_, index) => index !== widget));
}

export function withWidgetReplaced(spec: DashboardSpec, section: number, widget: number, next: Widget): DashboardSpec {
  return withWidgets(spec, section, spec.sections[section].widgets.map((held, index) => (index === widget ? next : held)));
}

export function withWidgetMoved(
  spec: DashboardSpec,
  from: { section: number; widget: number },
  toSection: number,
  placement: Placement,
): DashboardSpec {
  const moved = spec.sections[from.section].widgets[from.widget];
  return withWidgetAdded(withWidgetRemoved(spec, from.section, from.widget), toSection, moved, placement);
}

export function withSectionAdded(spec: DashboardSpec): DashboardSpec {
  return { ...spec, sections: [...spec.sections, { layout: 'grid', widgets: [] }] };
}

export function withSectionRemoved(spec: DashboardSpec, index: number): DashboardSpec {
  return { ...spec, sections: spec.sections.filter((_, at) => at !== index) };
}

export function withSectionMoved(spec: DashboardSpec, index: number, direction: 'earlier' | 'later'): DashboardSpec {
  const to = direction === 'earlier' ? index - 1 : index + 1;
  if (to < 0 || to >= spec.sections.length) return spec;
  const sections = [...spec.sections];
  [sections[index], sections[to]] = [sections[to], sections[index]];
  return { ...spec, sections };
}

/** An empty string, zero or undefined takes the key away rather than leaving an empty value the
 *  renderer would draw as a blank heading or a cadence of nothing. */
function written<T extends object>(held: T, patch: Partial<T>): T {
  const next = { ...held } as Record<string, unknown>;
  for (const [key, value] of Object.entries(patch)) {
    if (value === '' || value === 0 || value === undefined) delete next[key];
    else next[key] = value;
  }
  return next as T;
}

export function withSectionWritten(spec: DashboardSpec, index: number, patch: Partial<Pick<DashboardSection, 'title' | 'hint'>>): DashboardSpec {
  return withSection(spec, index, written<DashboardSection>(spec.sections[index], patch));
}

export function withPageWritten(
  spec: DashboardSpec,
  patch: Partial<Pick<DashboardSpec, 'title' | 'subtitle' | 'icon' | 'refreshSeconds'>>,
): DashboardSpec {
  return written<DashboardSpec>(spec, patch);
}

export function layoutItemsOf(section: DashboardSection): GridItem[] {
  return section.widgets.map((widget, index) => {
    const placement = widget.placement ?? { column: 0, row: 0, width: 1, height: 1 };
    return { i: String(index), x: placement.column, y: placement.row, w: placement.width, h: placement.height };
  });
}

/** The placements a grid reports, back in the section's order — the grid answers in its own. */
export function placementsFromLayout(layout: GridItem[], count: number): Placement[] {
  const placements: Placement[] = [];
  for (let index = 0; index < count; index += 1) {
    const item = layout.find((candidate) => candidate.i === String(index));
    if (item) placements[index] = { column: item.x, row: item.y, width: item.w, height: item.h };
  }
  return placements;
}

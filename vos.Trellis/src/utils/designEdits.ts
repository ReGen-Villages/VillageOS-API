import type { DashboardSection, DashboardSpecification, Placement, Widget } from '../types/dashboard';

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

function withSection(specification: DashboardSpecification, index: number, next: DashboardSection): DashboardSpecification {
  return { ...specification, sections: specification.sections.map((section, at) => (at === index ? next : section)) };
}

function withWidgets(specification: DashboardSpecification, index: number, widgets: Widget[]): DashboardSpecification {
  return withSection(specification, index, { ...specification.sections[index], widgets });
}

export function withWidgetAdded(specification: DashboardSpecification, section: number, widget: Widget, placement: Placement): DashboardSpecification {
  return withWidgets(specification, section, [...specification.sections[section].widgets, { ...widget, placement }]);
}

/** The grid reports its layout on every change and on none, so placements it did not move leave the
 *  specification as it was — a fresh specification for an unchanged layout would be drawn, reported,
 *  and written again without end. */
export function withPlacements(specification: DashboardSpecification, section: number, placements: Placement[]): DashboardSpecification {
  const held = specification.sections[section].widgets;
  const same = (a: Placement | undefined, b: Placement | undefined) =>
    a === b || (!!a && !!b && a.column === b.column && a.row === b.row && a.width === b.width && a.height === b.height);
  if (held.every((widget, index) => same(widget.placement, placements[index] ?? widget.placement))) return specification;
  return withWidgets(specification, section, held.map((widget, index) => ({ ...widget, placement: placements[index] ?? widget.placement })));
}

export function withWidgetRemoved(specification: DashboardSpecification, section: number, widget: number): DashboardSpecification {
  return withWidgets(specification, section, specification.sections[section].widgets.filter((_, index) => index !== widget));
}

export function withWidgetReplaced(specification: DashboardSpecification, section: number, widget: number, next: Widget): DashboardSpecification {
  return withWidgets(specification, section, specification.sections[section].widgets.map((held, index) => (index === widget ? next : held)));
}

export function withWidgetMoved(
  specification: DashboardSpecification,
  from: { section: number; widget: number },
  toSection: number,
  placement: Placement,
): DashboardSpecification {
  const moved = specification.sections[from.section].widgets[from.widget];
  return withWidgetAdded(withWidgetRemoved(specification, from.section, from.widget), toSection, moved, placement);
}

export function withSectionAdded(specification: DashboardSpecification): DashboardSpecification {
  return { ...specification, sections: [...specification.sections, { layout: 'grid', widgets: [] }] };
}

export function withSectionRemoved(specification: DashboardSpecification, index: number): DashboardSpecification {
  return { ...specification, sections: specification.sections.filter((_, at) => at !== index) };
}

export function withSectionMoved(specification: DashboardSpecification, index: number, direction: 'earlier' | 'later'): DashboardSpecification {
  const to = direction === 'earlier' ? index - 1 : index + 1;
  if (to < 0 || to >= specification.sections.length) return specification;
  const sections = [...specification.sections];
  [sections[index], sections[to]] = [sections[to], sections[index]];
  return { ...specification, sections };
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

export function withSectionWritten(specification: DashboardSpecification, index: number, patch: Partial<Pick<DashboardSection, 'title' | 'hint'>>): DashboardSpecification {
  return withSection(specification, index, written<DashboardSection>(specification.sections[index], patch));
}

export function withPageWritten(
  specification: DashboardSpecification,
  patch: Partial<Pick<DashboardSpecification, 'title' | 'subtitle' | 'icon' | 'refreshSeconds'>>,
): DashboardSpecification {
  return written<DashboardSpecification>(specification, patch);
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

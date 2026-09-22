import type {
  Binding, ComposedColumn, Composition, ComputedColumn, DashboardSpecification, RelationStep, TableColumn, TableWidget,
} from '../types/dashboard';
import type { ModelIndex } from '../api/dashboardApi';

/**
 * A table a person composed, and the roster it becomes.
 *
 * A composed table is a `thingList` binding with computed columns — exactly what a seeded roster
 * is — so it is drawn by the dashboard's own table and kept as the same `Dashboard` Thing. Nothing
 * here names a kind, a property or a state: every word comes from the choices, and the choices come
 * from what the model declares.
 */

/** The row's own identity, always the first column. */
const NAME_COLUMN = 'name';
const STATE_COLUMN = 'state';

/** The icon the sidebar draws beside a page the console kept, from the set it renders with. */
const PAGE_ICON = 'table-2';

function stepWord(step: RelationStep): string {
  const arrow = step.direction === 'in' ? `<${step.predicate}>` : `${step.predicate}>`;
  return `${arrow}${step.archetype ?? ''}`;
}

/** The key a column lands on. A property is its own name; a path names every hop it takes, so two
 *  paths to the same kind by different links stay two columns. */
export function columnKey(column: ComposedColumn): string {
  switch (column.source) {
    case 'property': return column.name;
    case 'state': return STATE_COLUMN;
    case 'path': {
      const hops = column.steps.map(stepWord).join(',');
      return column.property ? `path:${hops}.${column.property}` : `path:${hops}`;
    }
  }
}

/** What a column costs per row: nothing for a property or a state the rows share one read for, one
 *  walk per hop for a path. */
export function hopsOf(column: ComposedColumn): number {
  return column.source === 'path' ? column.steps.length : 0;
}

function tableColumn(column: ComposedColumn): TableColumn {
  const key = columnKey(column);
  switch (column.source) {
    case 'property': return { key, label: column.name, numeric: column.numeric };
    case 'state': return { key, label: STATE_COLUMN, render: 'badge' };
    case 'path': return { key, label: column.label };
  }
}

function computedColumn(column: ComposedColumn): ComputedColumn | null {
  const key = columnKey(column);
  switch (column.source) {
    case 'property': return null;
    case 'state': return { key, value: { kind: 'stateOf', states: column.states } };
    case 'path': {
      const value: Binding = column.property
        ? { kind: 'related', via: column.steps, property: column.property }
        : { kind: 'related', via: column.steps };
      return { key, value };
    }
  }
}

/** The state choices a composition can carry: none at a moment, since no read answers a state at
 *  an instant. */
function stateless(composition: Composition): Composition {
  if (!composition.moment) return composition;
  const columns = composition.columns.filter((column) => column.source !== 'state');
  const sortKey = columns.some((column) => columnKey(column) === composition.sortKey) ? composition.sortKey : undefined;
  return { ...composition, columns, inState: undefined, sortKey };
}

/** The table the dashboard draws for a composition, under the title it was given. */
export function tableOf(chosen: Composition, title: string): TableWidget {
  const composition = stateless(chosen);
  const rows: Binding = {
    kind: 'thingList',
    archetype: composition.kind,
    computed: composition.columns.map(computedColumn).filter((c): c is ComputedColumn => c !== null),
    ...(composition.inState ? { inState: composition.inState } : {}),
    ...(composition.where?.length ? { where: composition.where } : {}),
  };
  return {
    type: 'table',
    title,
    columns: [{ key: NAME_COLUMN, label: composition.kind, render: 'id' }, ...composition.columns.map(tableColumn)],
    rows,
    sortKey: composition.sortKey,
    sortDir: composition.sortDir,
    searchable: true,
    rowDetail: true,
    visibleRows: 25,
  };
}

/** A composed table kept as a page: one section holding the table, under the name the page was
 *  given, carrying the composition it was made from. */
export function pageOf(composition: Composition, name: string): DashboardSpecification {
  return {
    title: name,
    icon: PAGE_ICON,
    sections: [{ layout: 'single', widgets: [tableOf(composition, name)] }],
    composed: composition,
  };
}

/**
 * Every name the composition carries that the model does not declare, in the order the seed would
 * report them: the kind, then each column's links and far-end kinds, then each state. The seed
 * refuses a page naming a Thing it holds no declaration for or a state no range derives, and a
 * kept page has to pass what the seed would refuse — a kind removed since it was chosen is refused
 * here rather than written.
 */
export function unresolvedNames(composition: Composition, modelIndex: ModelIndex, declaredStates: ReadonlySet<string>): string[] {
  const unresolved: string[] = [];
  const thingNamed = (name: string | undefined) => {
    if (name && !modelIndex.byName.has(name)) unresolved.push(name);
  };
  const stateNamed = (name: string | undefined) => {
    if (name && !declaredStates.has(name)) unresolved.push(name);
  };
  thingNamed(composition.kind);
  for (const column of composition.columns) {
    if (column.source === 'path') {
      for (const step of column.steps) {
        thingNamed(step.predicate);
        thingNamed(step.archetype);
        stateNamed(step.inState);
        stateNamed(step.notInState);
      }
    }
    if (column.source === 'state') column.states.forEach(stateNamed);
  }
  stateNamed(composition.inState);
  return unresolved;
}

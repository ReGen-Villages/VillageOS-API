import type {
  Binding, ComposedColumn, Composition, ComputedColumn, DashboardSpecification, RelationStep, TableColumn, TableWidget,
} from '@tendril-ui/tendril';
import type { ModelIndex } from '@tendril-ui/tendril';

/**
 * A composed table is a `thingList` binding with computed columns, as a seeded roster is, so the dashboard's
 * own table draws it and it is kept as the same `Dashboard` Thing. Every word comes from choices the model
 * declares, so nothing here names a kind, a property or a state.
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

/** A path's key names every step, so two paths to one kind by different links stay two columns. */
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

/** None at an instant, because no read answers a state at an instant. */
function stateless(composition: Composition): Composition {
  if (!composition.moment) return composition;
  const columns = composition.columns.filter((column) => column.source !== 'state');
  const sortKey = columns.some((column) => columnKey(column) === composition.sortKey) ? composition.sortKey : undefined;
  return { ...composition, columns, inState: undefined, sortKey };
}

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
    sortDirection: composition.sortDirection,
    searchable: true,
    rowDetail: true,
    visibleRows: 25,
  };
}

export function pageOf(composition: Composition, name: string): DashboardSpecification {
  return {
    title: name,
    icon: PAGE_ICON,
    sections: [{ layout: 'single', widgets: [tableOf(composition, name)] }],
    composed: composition,
  };
}

/** In the order the seed would report them. The seed refuses a page naming an undeclared Thing or a state
 *  no range derives, so a kind removed since it was chosen is refused here rather than written. */
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

import type { Binding, Widget } from '@tendril-ui/tendril';

/** Answered once, because the panel offering row keys and the builder resolving `$scope` in a column both ask. */

export function rowsBindingOf(widget: Widget): Binding | undefined {
  switch (widget.type) {
    case 'table':
    case 'gantt':
    case 'action':
      return widget.rows;
    case 'leaderboard':
      return widget.entities;
    case 'funnel':
      return widget.stages.find((stage) => stage.drill)?.drill;
    default:
      return undefined;
  }
}

/** The kind of the rows a binding lists: the archetype it names, or the compared kind where it lists
 *  the compared Things. Nothing where the rows are a service's answer. */
export function rowKindOf(rows: Binding | undefined, compareKind: string | undefined): string | undefined {
  if (!rows) return undefined;
  if (rows.kind === 'stateList' || rows.kind === 'thingList') return rows.archetype;
  if (rows.kind === 'compareEntities') return compareKind;
  return undefined;
}

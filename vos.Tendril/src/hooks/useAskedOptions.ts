import type { AskedValue } from '../types/dashboard';
import { asRows, resolveBinding, type ResolveContext, type Row } from '../api/dashboardApi';
import { useBindings } from './useBinding';

/** The Things each of a widget's own choice fields offers, read once for the widget that asks — a
 *  list of rows each asking for the same reader would otherwise read the roster once per row. A
 *  field that chooses nothing resolves nothing and offers nothing. */
export function useAskedOptions(fields: AskedValue[], context: ResolveContext): Row[][] {
  const resolved = useBindings(
    fields.map((field) => (chooses(field) ? field.options : undefined)),
    context,
  );
  return resolved.map((state) => asRows(state.value));
}

/** The Things a choice's own fields offer for one row, read with that row as `$scope` when the
 *  choice is opened there, because the answer can differ from row to row. */
export function rostersForRow(fields: AskedValue[], context: ResolveContext, rowId: string): Promise<Row[][]> {
  const rowContext: ResolveContext = { ...context, scopeId: rowId };
  return Promise.all(
    fields.map(async (field) => (chooses(field) && field.options ? asRows(await resolveBinding(field.options, rowContext)) : [])),
  );
}

/** Whether the field is answered by choosing from a roster rather than by typing. */
const chooses = (field: AskedValue) => field.kind === 'choice' || field.kind === 'multichoice';

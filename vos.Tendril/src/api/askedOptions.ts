import type { AskedValue } from '../types/dashboard';
import { asRows, resolveBinding, type ResolveContext, type Row } from './dashboardApi';

/** Whether the field is answered by choosing from a roster rather than by typing. */
export const chooses = (field: AskedValue) => field.kind === 'choice' || field.kind === 'multichoice';

/** The Things a choice's own fields offer for one row, read with that row as `$scope` when the
 *  choice is opened there, because the answer can differ from row to row. */
export function rostersForRow(fields: AskedValue[], context: ResolveContext, rowId: string): Promise<Row[][]> {
  const rowContext: ResolveContext = { ...context, scopeId: rowId };
  return Promise.all(
    fields.map(async (field) => (chooses(field) && field.options ? asRows(await resolveBinding(field.options, rowContext)) : [])),
  );
}

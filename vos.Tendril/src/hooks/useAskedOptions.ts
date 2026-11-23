import type { AskedValue } from '../types/dashboard';
import { chooses } from '../api/askedOptions';
import { asRows, type ResolveContext, type Row } from '../api/dashboardApi';
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

import type { AskedValue } from '../types/dashboard';
import { asRows, type ResolveContext, type Row } from '../api/dashboardApi';
import { useBindings } from './useDashboard';

/** The Things each choice field offers, read once for the widget that asks — a list of rows each
 *  asking for the same reader would otherwise read the roster once per row. A field that chooses
 *  nothing resolves nothing and offers nothing. */
export function useAskedOptions(fields: AskedValue[], context: ResolveContext): Row[][] {
  const resolved = useBindings(
    fields.map((field) => (chooses(field) ? field.options : undefined)),
    context,
  );
  return resolved.map((state) => asRows(state.value));
}

/** Whether the field is answered by choosing from a roster rather than by typing. */
const chooses = (field: AskedValue) => field.kind === 'choice' || field.kind === 'multichoice';

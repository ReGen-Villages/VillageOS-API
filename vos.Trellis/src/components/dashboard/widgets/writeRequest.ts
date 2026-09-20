import type { ActionChoice, ActionWidget, AskedValue, FormWidget } from '../../../types/dashboard';
import type { Row } from '../../../api/dashboardApi';

/** What a person has entered so far, by the key each value is sent under. A field taking several
 *  names at once holds them as a list; every other field holds what was typed or chosen. */
export type Entered = Record<string, string | string[]>;

function chosen(entered: Entered, key: string): string[] {
  const value = entered[key];
  if (Array.isArray(value)) return value.filter((name) => name.trim() !== '');
  return (value ?? '').trim() === '' ? [] : [String(value).trim()];
}

/** The name a row is read by — what a person sees they are deciding about, and what the endpoint
 *  resolves the row from. Names travel on the wire; the endpoint holds the ids. */
export function nameOf(widget: ActionWidget, row: Row): string {
  return String(row[widget.label ?? 'name'] ?? row.id ?? '');
}

/** Whether every value a person must supply has been. Blank space counts as nothing, and so does
 *  a field where nothing was chosen. */
export function complete(fields: AskedValue[] | undefined, entered: Entered): boolean {
  return (fields ?? []).every((field) => field.optional || chosen(entered, field.key).length > 0);
}

/** A value as the endpoint receives it: the names chosen where the field takes several, a number
 *  where it asks for one, text otherwise, and nothing where an optional field was left empty. */
function toSend(field: AskedValue, entered: Entered): number | string | string[] | undefined {
  const chosenNames = chosen(entered, field.key);
  if (chosenNames.length === 0) return undefined;
  if (field.kind === 'multichoice') return chosenNames;
  return field.kind === 'number' ? Number(chosenNames[0]) : chosenNames[0];
}

function withValues(body: Record<string, unknown>, fields: AskedValue[] | undefined, entered: Entered): Record<string, unknown> {
  for (const field of fields ?? []) {
    const value = toSend(field, entered);
    if (value !== undefined) body[field.key] = value;
  }
  return body;
}

/** The request a press on a row makes: the row by name, the choice as the reason it names or the
 *  act it is, and what was asked for the row. No actor of any kind: the session the request travels
 *  under is the only answer to who is asking, and the platform is what holds it. */
export function actionRequest(widget: ActionWidget, row: Row, choice: ActionChoice, entered: Entered): Record<string, unknown> {
  const body: Record<string, unknown> = {};
  if (choice.act) body.view = choice.act;
  body.record = nameOf(widget, row);
  if (choice.target) body.reason = choice.target;
  return withValues(body, widget.asks, entered);
}

/** The request a form makes: the act by name and every field a person filled. `act` names which
 *  of the form's two acts is being made — the press it writes under, or the read its preview
 *  offers, which asks the same fields the same way. */
export function formRequest(widget: FormWidget, entered: Entered, act: string = widget.writes.act): Record<string, unknown> {
  return withValues({ view: act }, widget.fields, entered);
}

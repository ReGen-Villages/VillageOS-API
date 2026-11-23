import type { ActionChoice, ActionWidget, AskedValue, FormWidget } from '../types/dashboard';
import type { Row } from '../api/dashboardApi';
import { isPlatformRoute, postToEndpoint, type EndpointAnswer } from '../api/dashboardWrites';
import type { ModelReads } from '../api/modelReads';
import { PROPERTY_NAME_WORDING } from '../api/propertyNames';
import { wordingIn, wordsInLanguage } from '../i18n/termWording';
import { tendrilWord } from '../i18n/words';

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
 *  under is the only answer to who is asking, and the platform is what holds it.
 *
 *  A route on the platform is also sent the row's id, because names are not unique; an endpoint
 *  forwarded to a project's own service is sent names only, since such a service may refuse a field
 *  it does not know. */
function actionRequest(widget: ActionWidget, row: Row, choice: ActionChoice, entered: Entered, route: string): Record<string, unknown> {
  const body: Record<string, unknown> = {};
  if (choice.act) body.view = choice.act;
  body.record = nameOf(widget, row);
  if (isPlatformRoute(route) && row.id !== undefined) body.recordId = String(row.id);
  if (choice.target) body.reason = choice.target;
  return withValues(body, asksOf(widget, choice), entered);
}

export function asksOf(widget: ActionWidget, choice: ActionChoice): AskedValue[] {
  return choice.asks ?? widget.asks ?? [];
}

/** One button on a row: the choice it makes, and the Thing a press relates the row to where the
 *  model offered it rather than the specification.
 *
 *  `key` names the choice to a caller that lists the choices in one call and presses one in another:
 *  the Thing a choice the model offered stands for, otherwise the place of a listed choice in the
 *  page description. A place holds only while the description is unchanged, so such a caller has to
 *  notice a description edited in between. */
export interface OfferedChoice {
  key: string;
  choice: ActionChoice;
  drawnId?: string;
}

/** The model's choices in the order the binding returned them, each replaced by a listed choice
 *  naming it, then the listed choices naming none of them. */
export function offeredChoices(widget: ActionWidget, drawn: Row[], wordsOf: (row: Row) => string): OfferedChoice[] {
  const listed = widget.writes.choices;
  const drawnNames = new Set(drawn.map((row) => String(row.name ?? '')));
  const asListed = (choice: ActionChoice): OfferedChoice => ({ key: `listed:${listed.indexOf(choice)}`, choice });
  const fromTheModel = drawn.map((row): OfferedChoice => {
    const replacing = listed.find((choice) => choice.target !== undefined && choice.target === row.name);
    const drawnId = String(row.id ?? '');
    return replacing ? asListed(replacing) : { key: drawnId, choice: { label: wordsOf(row) }, drawnId };
  });
  const listedOnly = listed
    .filter((choice) => choice.target === undefined || !drawnNames.has(choice.target))
    .map(asListed);
  return [...fromTheModel, ...listedOnly];
}

/** What a choice the model offers is called in a language: the drawn Thing's own wording, or its name. */
export function choiceLabelOf(drawn: Row, language: string): string {
  return wordsInLanguage(wordingIn(drawn[PROPERTY_NAME_WORDING]) ?? undefined, language) ?? String(drawn.name ?? '');
}

/** Where a press is posted, or nothing where it relates the row to a drawn Thing instead. */
function routeFor(widget: ActionWidget, offered: OfferedChoice): string | undefined {
  if (offered.choice.via) return offered.choice.via;
  return offered.drawnId === undefined ? widget.writes.via : undefined;
}

/** One write a press makes. A post names its route as the page description does. */
export type PressWrite =
  | { write: 'post'; via: string; body: Record<string, unknown> }
  | { write: 'relate'; predicate: string; subjectId: string; predicateId: string; targetId: string }
  | { write: 'stamp'; thingId: string; property: string };

/**
 * The writes a press on a row makes, in the order they are made, and nothing sent: a post to the
 * route the choice or the widget names, or the row related to the Thing a choice the model offered
 * stands for, and then a stamp on the row where the widget stamps. Refused in words where the model
 * holds no predicate to relate along.
 */
export function pressOf(
  widget: ActionWidget,
  row: Row,
  offered: OfferedChoice,
  entered: Entered,
  predicateIdOf: (predicate: string) => string | undefined,
): { writes: PressWrite[] } | { refusal: string } {
  const rowId = String(row.id ?? '');
  const writes: PressWrite[] = [];
  const route = routeFor(widget, offered);
  if (route) {
    writes.push({ write: 'post', via: route, body: actionRequest(widget, row, offered.choice, entered, route) });
  } else {
    const predicate = widget.writes.choicesFrom?.predicate ?? '';
    const predicateId = predicateIdOf(predicate);
    if (!predicateId) return { refusal: tendrilWord('cannotRelate', { predicate }) };
    writes.push({ write: 'relate', predicate, subjectId: rowId, predicateId, targetId: offered.drawnId! });
  }
  if (widget.writes.stamps) writes.push({ write: 'stamp', thingId: rowId, property: widget.writes.stamps });
  return { writes };
}

/** Sends the writes a press composed, in order, and stops at the first one refused, so a refused
 *  press is never stamped. Answers what the endpoint said on taking a post, or the refusal. A page
 *  that cannot stamp leaves the stamp out. */
export async function sendPress(
  writes: PressWrite[],
  reads: ModelReads,
  wordRefusal?: (answer: Record<string, unknown>) => string | undefined,
): Promise<EndpointAnswer> {
  let answer: EndpointAnswer = {};
  try {
    for (const write of writes) {
      if (write.write === 'post') {
        answer = await postToEndpoint(reads, write.via, write.body, wordRefusal);
        if (answer.error) return answer;
      } else if (write.write === 'relate') {
        if (!reads.relate) return { error: tendrilWord('cannotRelate', { predicate: write.predicate }) };
        await reads.relate(write.subjectId, write.predicateId, write.targetId);
      } else if (reads.stampTime) {
        await reads.stampTime(write.thingId, write.property, new Date());
      }
    }
  } catch (refused) {
    return { error: refused instanceof Error ? refused.message : String(refused) };
  }
  return answer;
}

/** The request a form makes: the act by name and every field a person filled. `act` names which
 *  of the form's two acts is being made — the press it writes under, or the read its preview
 *  offers, which asks the same fields the same way. */
export function formRequest(widget: FormWidget, entered: Entered, act: string): Record<string, unknown> {
  return withValues({ view: act }, widget.fields, entered);
}

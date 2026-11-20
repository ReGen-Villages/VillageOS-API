/**
 * A specification can be written for a newer console than the one reading it, and ignoring what it does not
 * know lets a model and a console be released separately. That is right for presentation and wrong for a binding, where
 * half an answer is a number rather than a gap: an unknown kind draws an empty state that reads as a missing
 * value, and an unknown field draws a figure in the right units and the wrong size. So each binding is judged
 * here before it is resolved, and a widget asking for a word this build cannot answer says so instead of drawing.
 */
import type { Binding, Widget } from '@tendril-ui/tendril';
import { bindingsOf } from './dashboardSubscription';

/** A mapping over the vocabulary, so a kind left out fails the build, and so does a misspelt field or one
 *  its kind lacks. `npm run build` enforces it; the tests transpile types away. `kind` is left out because
 *  every binding carries it, and `READS` puts it back. */
type BindingFields = {
  [K in Binding['kind']]: readonly Exclude<keyof Extract<Binding, { kind: K }>, 'kind'>[];
};

export const BINDING_FIELDS: BindingFields = {
  const: ['value'],
  stateCount: ['state', 'scope', 'archetype', 'excludeState'],
  stateList: ['state', 'excludeState', 'scope', 'limit', 'archetype', 'properties', 'computed'],
  thingList: ['archetype', 'scope', 'limit', 'computed', 'inState', 'where'],
  aggregate: ['archetype', 'reduction', 'property', 'where', 'scope'],
  property: ['thing', 'property'],
  related: ['via', 'thing', 'property'],
  value: ['thing', 'via', 'property'],
  stateOf: ['states', 'thing'],
  verdict: ['states', 'thing', 'via'],
  origin: ['property', 'thing', 'via', 'reads', 'source'],
  working: ['property', 'thing', 'via'],
  ratio: ['numerator', 'denominator'],
  compareEntities: ['properties', 'computed'],
  timeseries: ['archetype', 'happenedAt', 'property', 'reduction', 'bucketSeconds', 'buckets', 'bucketsPerPoint', 'scope'],
  latest: ['series'],
  service: ['endpoint', 'body', 'select'],
  history: ['property', 'windowSeconds', 'steps', 'bins', 'pairedWith', 'answer'],
};

/** Built once, because it is asked of every widget on every refresh. */
const READS: ReadonlyMap<string, ReadonlySet<string>> = new Map(
  Object.entries(BINDING_FIELDS).map(([kind, fields]) => [kind, new Set<string>([...fields, 'kind'])]),
);

/** An unknown kind is reported alone, because there is no entry to judge its fields against and listing them
 *  would hide the one word that explains them. A binding with no kind reports the empty string, because every
 *  word here reaches a reader inside a translated sentence, and saying "no kind" is that sentence's job. */
function unansweredBy(binding: Binding): string[] {
  const kind = (binding as { kind?: unknown }).kind;
  if (typeof kind !== 'string') return [''];
  const read = READS.get(kind);
  return read ? Object.keys(binding).filter((field) => !read.has(field)) : [kind];
}

/** Nested bindings are judged too, because a ratio's halves and a tile's series are resolved like any other
 *  binding. An empty answer says nothing about whether the model holds values for them. */
export function unimplementedWordsIn(widget: Widget): string[] {
  const found = bindingsOf(widget).flatMap(unansweredBy);
  return [...new Set(found)];
}

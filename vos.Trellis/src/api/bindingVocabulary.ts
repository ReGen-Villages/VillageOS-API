/**
 * The binding vocabulary this build can answer, and what a widget asks for that it cannot.
 *
 * A spec is model data. It can be authored against a newer client than the one reading it, and the
 * client's tolerance for what it does not recognise is what lets a model and a console ship apart.
 * That tolerance is right for presentation and wrong for a binding: a binding is a question, and
 * answering half of one produces a number rather than a gap. A tile whose kind this build lacks
 * draws its ordinary empty state, which reads as a value the model has not got; a field this build
 * does not read is worse, because the widget draws a figure in the right units and the wrong size.
 *
 * So a binding is judged against the vocabulary before anything tries to resolve it, and a widget
 * asking for a word this build has no answer for says so instead of drawing.
 */
import type { Binding, Widget } from '../types/dashboard';
import { bindingsOf } from './dashboardSubscription';

/**
 * Every field each kind reads, besides the `kind` naming it.
 *
 * A mapping over the vocabulary rather than a plain object, which buys two things a hand-kept list
 * cannot. A kind added to the vocabulary and left out here **fails the build**, so this table cannot
 * fall quietly behind — and it cannot be merely short, which is how a checked list usually decays.
 * And each entry may only name real fields of its own kind, so a misspelling, or a field copied
 * from the entry above, is refused rather than becoming a word that is never matched.
 *
 * `Exclude` keeps the discriminant out: it is on every binding, so naming it would be the same word
 * repeated once per entry. {@link unimplementedWordsIn} puts it back when it judges a real binding,
 * which carries it.
 *
 * Note this is enforced by `npm run build` and not by the tests, which transpile types away.
 */
type BindingFields = {
  [K in Binding['kind']]: readonly Exclude<keyof Extract<Binding, { kind: K }>, 'kind'>[];
};

export const BINDING_FIELDS: BindingFields = {
  const: ['value'],
  stateCount: ['state', 'scope', 'archetype'],
  stateList: ['state', 'excludeState', 'scope', 'limit', 'archetype', 'properties', 'computed'],
  thingList: ['archetype', 'scope', 'limit', 'computed'],
  aggregate: ['archetype', 'op', 'property', 'where', 'scope'],
  property: ['thing', 'property'],
  related: ['via', 'thing', 'property'],
  stateOf: ['states', 'thing'],
  verdict: ['states', 'thing', 'via'],
  origin: ['property', 'thing', 'via', 'reads', 'source'],
  working: ['property', 'thing', 'via'],
  ratio: ['numerator', 'denominator'],
  compareEntities: ['properties', 'computed'],
  timeseries: ['archetype', 'happenedAt', 'property', 'op', 'bucketSeconds', 'buckets', 'bucketsPerPoint', 'scope'],
  latest: ['series'],
  service: ['endpoint', 'body', 'select'],
};

/** Each kind's fields as a set, with the discriminant every binding carries put back. Built once
 *  rather than per binding, because this is asked of every widget on every refresh. */
const READS: ReadonlyMap<string, ReadonlySet<string>> = new Map(
  Object.entries(BINDING_FIELDS).map(([kind, fields]) => [kind, new Set<string>([...fields, 'kind'])]),
);

/** What one binding asks for that this build cannot answer: its kind, when the vocabulary has no
 *  such word, or each field the kind does not read. A binding of an unknown kind is reported by its
 *  kind alone — there is no entry to judge its fields against, and listing them all would bury the
 *  one word that explains the rest.
 *
 *  A binding naming no kind at all reports the empty string: there is no word to name, and saying
 *  so is the caller's to word, not this module's — every string here reaches a reader through a
 *  translated sentence. */
function unansweredBy(binding: Binding): string[] {
  const kind = (binding as { kind?: unknown }).kind;
  if (typeof kind !== 'string') return [''];
  const read = READS.get(kind);
  return read ? Object.keys(binding).filter((field) => !read.has(field)) : [kind];
}

/**
 * Every word this widget asks for that this build has no answer for, in the order they were found
 * and each named once.
 *
 * Judged over the widget's own binding slots and everything nested inside them, because a nested
 * binding is a question in its own right: a ratio's half and the series a tile reads its newest
 * point from are resolved exactly as a top-level binding is.
 *
 * An empty answer means every binding is one this build can resolve. It says nothing about whether
 * the model holds values for them.
 */
export function unimplementedWordsIn(widget: Widget): string[] {
  const found = bindingsOf(widget).flatMap(unansweredBy);
  return [...new Set(found)];
}

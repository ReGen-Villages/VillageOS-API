/**
 * How a state read is narrowed, and the request one narrowed read makes.
 *
 * Apart from `stateApi` so that a reader which resolves bindings without reaching the broker — the
 * findings page a submitter opens, holding no credential — can still say what question was asked.
 */

/** How a state answer is narrowed on the server: which Things come back, and which of their values
 *  ride along. Every field is one the endpoint accepts, so a caller narrows by asking a smaller
 *  question rather than by throwing most of a large answer away. */
export interface StateNarrowing {
  /** Further states a Thing must hold as well as the one being asked about. */
  alsoIn?: string[];
  /** States that disqualify a Thing — how a caller asks for the absence of one. */
  notIn?: string[];
  /** Only Things that `is` this type, followed through the whole chain. */
  type?: string;
  /** Only Things this container reaches through {@link withinPredicate}, at any depth. */
  within?: string;
  /** The predicate the containment is written with; the caller names it, so nothing here carries a
   *  vocabulary of its own. */
  withinPredicate?: string;
  /** The kinds Things `is` hold states too, and are left out unless this asks for them. */
  includeArchetypes?: boolean;
  /** The most Things to answer with, taken in name order. */
  limit?: number;
  /** Property names returned beside each id, so a row arrives complete. */
  properties?: string[];
}

/** The request one narrowed read makes. Exported because a caller sharing one in-flight request per
 *  question needs to know what the question is: two widgets narrowing the same state differently
 *  must not be handed each other's answer. */
export function thingsInStatePath(stateName: string, narrowing?: StateNarrowing): string {
  const path = `/api/states/${encodeURIComponent(stateName)}/things`;
  if (!narrowing) return path;
  const query = new URLSearchParams();
  if (narrowing.alsoIn?.length) query.set('alsoIn', narrowing.alsoIn.join(','));
  if (narrowing.notIn?.length) query.set('notIn', narrowing.notIn.join(','));
  if (narrowing.type) query.set('type', narrowing.type);
  // The endpoint refuses a container without the predicate it is reached by, so half a scope is not
  // a question worth spending a request on.
  if (narrowing.within && narrowing.withinPredicate) {
    query.set('within', narrowing.within);
    query.set('withinPredicate', narrowing.withinPredicate);
  }
  if (narrowing.includeArchetypes) query.set('includeArchetypes', 'true');
  if (narrowing.limit !== undefined) query.set('limit', String(narrowing.limit));
  if (narrowing.properties?.length) query.set('properties', narrowing.properties.join(','));
  const suffix = query.toString();
  return suffix ? `${path}?${suffix}` : path;
}

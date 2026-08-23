/**
 * What a subscription covers, in the shape `POST /api/subscriptions` reads.
 *
 * The platform resolves a selector into a set of Things and the relationships between them, hands
 * that back as a snapshot, and streams the later changes to those objects and no others. A page
 * therefore says what it is about once, and both what it starts with and what reaches it afterwards
 * follow from that one statement.
 */
import type { VosThing, VosRelationship } from './vos';

/** Which way an edge is followed from the Things selected so far. */
export type TraverseDirection = 'outgoing' | 'incoming' | 'both';

/** One edge to follow out from everything the selector has reached by the time the rule runs. */
export interface TraverseRule {
  predicate: string;
  direction?: TraverseDirection;
  /** How many hops to follow. Omitted means one. */
  depth?: number;
}

export interface SubscriptionSelector {
  /** The whole model, and every object created after the subscription opened. Every other field is
   *  ignored when this is set. */
  all?: boolean;
  /** Thing ids. The platform reads these as identifiers, so a name here is refused, not looked up. */
  ids?: string[];
  names?: string[];
  /** Things that `is` one of these, followed through the whole chain. */
  types?: string[];
  /** Applied in order, each over everything selected before it. */
  traverse?: TraverseRule[];
  /** Include what each selected Thing `is`, so the values it inherits resolve. Default true. */
  includeIsAncestors?: boolean;
  /** Include the edges between the selected Things. Default true. */
  includeRelationships?: boolean;
}

/** What a page that reads across the whole model asks for — the graph, the explorer, the searches.
 *  Alone among the selectors it also covers objects created later, because the platform resolves
 *  every other one into a fixed set when it opens. */
export const WHOLE_MODEL: SubscriptionSelector = { all: true };

/** A subscription that has just opened, and the objects it answered with as they stood at
 *  `watermark`. */
export interface SubscriptionOpened {
  subscriptionId: string;
  watermark: number;
  /**
   * The objects the selector reached — or null for {@link WHOLE_MODEL}, whose snapshot is left
   * unread.
   *
   * A whole-model subscription's snapshot is not what fills the store: the model read is, because
   * only that honours the properties the model says its pages are drawn with. Converting a
   * whole-model snapshot into the shape the store holds and then discarding it costs an object per
   * Thing and per property on the largest answer the platform gives.
   */
  covered: { things: VosThing[]; relationships: VosRelationship[] } | null;
}

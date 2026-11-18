/**
 * What a subscription covers, in the shape `POST /api/subscriptions` reads.
 *
 * The platform resolves a selector into a set of Things and the relationships between them, hands
 * that back as a snapshot, and streams the later changes to those objects and no others. A page
 * therefore says what it is about once, and both what it starts with and what reaches it afterwards
 * follow from that one statement.
 */
import type { SubscriptionSelector, VosThing, VosRelationship } from '@tendril-ui/tendril';

export type { SubscriptionSelector, TraverseDirection, TraverseRule } from '@tendril-ui/tendril';

/** What a page that reads across the whole model asks for — the graph, the explorer, the searches.
 *  Alone among the selectors it also covers objects created later, because the platform resolves
 *  every other one into a fixed set when it opens. */
export const WHOLE_MODEL: SubscriptionSelector = { all: true };

/** The objects a selector reached, as they stood when the platform answered. */
export interface CoveredObjects {
  things: VosThing[];
  relationships: VosRelationship[];
  /** The derived states each Thing held, empty for a Thing holding none. */
  thingStates: Map<string, string[]>;
}

/** A subscription that has just opened, and the objects it answered with as they stood at
 *  `watermark`. */
export interface SubscriptionOpened {
  subscriptionId: string;
  watermark: number;
  /**
   * The objects the selector reached — or null for {@link WHOLE_MODEL}, which asks for no snapshot.
   * The model read fills the store for it instead, because only that honours the properties the
   * model says its pages are drawn with.
   */
  covered: CoveredObjects | null;
}

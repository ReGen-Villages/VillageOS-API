/** The platform resolves a selector into Things and their relationships, answers with them, and then streams
 *  changes to those alone, so a page says once what it is about. */
import type { SubscriptionSelector, VosThing, VosRelationship } from '@tendril-ui/tendril';

export type { SubscriptionSelector } from '@tendril-ui/tendril';

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
  /** Null for {@link WHOLE_MODEL}, which asks for no snapshot, because the model read fills the store and only
   *  it honours the properties the model says its pages use. */
  covered: CoveredObjects | null;
}

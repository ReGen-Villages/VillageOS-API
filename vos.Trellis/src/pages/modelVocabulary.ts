/**
 * How this client reads a model's own vocabulary: the Thing that owns a mark, and the terms declared
 * under it. Every page that finds something by a mark reads it through here, so there is one answer to
 * what owning a mark means rather than one per page.
 *
 * Nothing here names an archetype. A vocabulary is found by the mark its archetype owns, so a model that
 * renamed the archetype keeps answering and a project that declares a term of its own is offered it
 * without a line changing here.
 */

import type { EffectiveProperty, VosRelationship, VosThing } from '../types/vos';

/** What a programme allocation is for. The intake service resolves a submitted word against the Things
 *  under whichever archetype carries this. */
export const ALLOCATION_CATEGORY_ARCHETYPE_FLAG = '__IsAllocationCategoryArchetype';
export const HAZARD_TYPE_ARCHETYPE_FLAG = '__IsHazardTypeArchetype';
export const HAZARD_LEVEL_ARCHETYPE_FLAG = '__IsHazardLevelArchetype';

/** The platform's one canonical predicate, and the only predicate name a reader may hold: it is the
 *  platform's own vocabulary rather than any model's, and nothing marks it. */
export const IS_PREDICATE_NAME = 'is';

/** A whole model as a page reads it. Properties come from the server resolved effective rather than own:
 *  seed normalization moves a Thing's own values into its overrides, and a reader looking only at own
 *  properties finds a model full of Things and reads nothing off them. Each property says which of the
 *  two it is, which is what lets a mark still be told from what inherited it. */
export interface ModelReading {
  things: readonly VosThing[];
  relationships: readonly VosRelationship[];
  properties: Readonly<Record<string, Record<string, EffectiveProperty>>>;
}

/** The terms declared under the archetype carrying a mark, by name, in the order a list should show
 *  them. A model declaring the mark on nothing, or on more than one Thing, has no vocabulary this can
 *  read — two carriers leave no way to say which one a term belongs to, so it answers with none rather
 *  than picking. */
export function termsMarked(reading: ModelReading, archetypeFlag: string): string[] {
  const archetype = ownCarrierOf(reading, archetypeFlag);
  if (!archetype) return [];

  const names = new Map(reading.things.map((thing) => [thing.Id, thing]));
  const isEdge = new Set(
    reading.things.filter((thing) => thing.Name === IS_PREDICATE_NAME).map((thing) => thing.Id),
  );

  // Outwards from the archetype rather than upwards from every Thing, and through intermediate types,
  // so a project grouping its categories under one of its own still has them offered.
  const reached = new Set([archetype]);
  const frontier = [archetype];
  const terms: string[] = [];
  while (frontier.length > 0) {
    const current = frontier.pop()!;
    for (const edge of reading.relationships) {
      if (edge.TargetId !== current || !isEdge.has(edge.PredicateId)) continue;
      if (reached.has(edge.SubjectId)) continue;

      reached.add(edge.SubjectId);
      frontier.push(edge.SubjectId);
      const term = names.get(edge.SubjectId);
      if (term && !term.IsArchetype) terms.push(term.Name);
    }
  }
  return terms.sort((left, right) => left.localeCompare(right));
}

/**
 * The one Thing that owns a mark.
 *
 * Own, not effective: a mark is an ordinary property on the archetype, so every term that `is` it
 * inherits the mark too. Counting inherited carriers finds one archetype and all of its terms, and a
 * vocabulary then reads as ambiguous the moment it has any terms at all.
 */
export function ownCarrierOf(reading: ModelReading, flag: string): string | null {
  const owning = Object.keys(reading.properties).filter(
    (id) => reading.properties[id][flag]?.IsInherited === false,
  );
  return owning.length === 1 ? owning[0] : null;
}

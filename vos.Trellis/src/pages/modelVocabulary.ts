/**
 * How this client reads a model's own vocabulary: the Thing a role binds, and the terms declared under it.
 * Every page that finds something through a role reads it through here, so there is one answer to what a
 * binding means rather than one per page.
 *
 * Nothing here names an archetype. A vocabulary is found through the role Thing that binds its archetype,
 * so a model that renamed the archetype keeps answering and a project that declares a term of its own is
 * offered it without a line changing here.
 */

import type { EffectiveProperty, VosRelationship, VosThing } from '../types/vos';
import { BINDS_PREDICATE_NAME, LAND_INTAKE_ROLE } from '../api/landIntakeRoles';
import { wordingIn, type TermWording } from '../i18n/termWording';

/** What a programme allocation is for. The intake service resolves a submitted word against the Things
 *  under whichever archetype this role binds. */
export const ALLOCATION_CATEGORY_ROLE = LAND_INTAKE_ROLE.AllocationCategory;
export const HAZARD_TYPE_ROLE = LAND_INTAKE_ROLE.HazardType;
export const HAZARD_LEVEL_ROLE = LAND_INTAKE_ROLE.HazardLevel;

/** The platform's one canonical predicate, and with `binds` the only predicate name a reader may hold: it
 *  is the platform's own vocabulary rather than any model's, and nothing binds it. */
export const IS_PREDICATE_NAME = 'is';

/** A whole model as a page reads it. Properties come from the server resolved effective rather than own:
 *  seed normalization moves a Thing's own values into its overrides, and a reader looking only at own
 *  properties finds a model full of Things and reads nothing off them. */
export interface ModelReading {
  things: readonly VosThing[];
  relationships: readonly VosRelationship[];
  properties: Readonly<Record<string, Record<string, EffectiveProperty>>>;
}

/** The terms declared under the archetype a role binds, by name, in the order a list should show
 *  them. A model binding the role to nothing, or to more than one Thing, has no vocabulary this can
 *  read — two bindings leave no way to say which one a term belongs to, so it answers with none rather
 *  than picking. */
export function termsBound(reading: ModelReading, roleId: string): string[] {
  return termThingsBound(reading, roleId).map((term) => term.Name);
}

/** The property a term states its words in, as JSON text of words by language. */
const WORDING_PROPERTY = 'wording';

/** What each term under the given roles is called in each language, read from the term's own `wording`
 *  property. A term stating none is left out, and is shown by its name. */
export function termWordingBound(reading: ModelReading, roleIds: readonly string[]): TermWording {
  const wording: Record<string, Record<string, string>> = {};
  for (const roleId of roleIds) {
    for (const term of termThingsBound(reading, roleId)) {
      const words = wordingIn(reading.properties[term.Id]?.[WORDING_PROPERTY]?.Value);
      if (words && !(term.Name in wording)) wording[term.Name] = words;
    }
  }
  return wording;
}

function termThingsBound(reading: ModelReading, roleId: string): VosThing[] {
  const archetype = boundBy(reading, roleId);
  if (!archetype) return [];

  const names = new Map(reading.things.map((thing) => [thing.Id, thing]));
  const isEdge = new Set(
    reading.things.filter((thing) => thing.Name === IS_PREDICATE_NAME).map((thing) => thing.Id),
  );

  // Outwards from the archetype rather than upwards from every Thing, and through intermediate types,
  // so a project grouping its categories under one of its own still has them offered.
  const reached = new Set([archetype]);
  const frontier = [archetype];
  const terms: VosThing[] = [];
  while (frontier.length > 0) {
    const current = frontier.pop()!;
    for (const edge of reading.relationships) {
      if (edge.TargetId !== current || !isEdge.has(edge.PredicateId)) continue;
      if (reached.has(edge.SubjectId)) continue;

      reached.add(edge.SubjectId);
      frontier.push(edge.SubjectId);
      const term = names.get(edge.SubjectId);
      if (term && !term.IsArchetype) terms.push(term);
    }
  }
  return terms.sort((left, right) => left.Name.localeCompare(right.Name));
}

/**
 * The one Thing a role binds, read from the role Thing's own `binds` relationships and never from a name.
 * A role the model binds to nothing, or binds twice, answers with none: two bindings leave nothing able to
 * say which Thing plays the role, and seed validation refuses such a model.
 */
export function boundBy(reading: ModelReading, roleId: string): string | null {
  const binds = new Set(
    reading.things.filter((thing) => thing.Name === BINDS_PREDICATE_NAME).map((thing) => thing.Id),
  );
  const bound = reading.relationships
    .filter((edge) => edge.SubjectId === roleId && binds.has(edge.PredicateId))
    .map((edge) => edge.TargetId);
  return bound.length === 1 ? bound[0] : null;
}

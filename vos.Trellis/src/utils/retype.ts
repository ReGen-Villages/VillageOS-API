import { relationshipApi } from '../api/relationshipApi';
import type { VosRelationship } from '../types/vos';

// Repoint ONE of a Thing's types to a different archetype (#5860). Multiple inheritance is a first-class
// feature, so this never collapses the rest: with `fromArchetypeId` it replaces only that type edge; without
// it (the single-type case) it replaces the Thing's lone type edge. The human-in-the-loop reclassification —
// e.g. a mis-classified element becomes a SolarArray, changing what reactive roll-ups and analysis pick up.
export async function retypeThing(
  thingId: string,
  newArchetypeId: string,
  isPredicateId: string,
  relationships: VosRelationship[],
  fromArchetypeId?: string,
): Promise<void> {
  const typeEdges = relationships.filter(
    (r) => r.SubjectId === thingId && r.PredicateId === isPredicateId,
  );
  const toRemove = fromArchetypeId
    ? typeEdges.filter((r) => r.TargetId === fromArchetypeId)
    : typeEdges;
  for (const edge of toRemove) {
    await relationshipApi.remove(edge.Id);
  }
  await relationshipApi.create(thingId, isPredicateId, newArchetypeId);
}

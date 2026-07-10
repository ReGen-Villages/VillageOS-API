import { relationshipApi } from '../api/relationshipApi';
import type { VosRelationship } from '../types/vos';

// Repoint a Thing's type in one action (#5860): remove its current is-edges and add an is-edge to the new
// archetype, leaving exactly one type edge. The human-in-the-loop reclassification — e.g. a mis-classified
// element becomes a SolarArray, changing what reactive roll-ups and analysis pick it up.
export async function retypeThing(
  thingId: string,
  newArchetypeId: string,
  isPredicateId: string,
  relationships: VosRelationship[],
): Promise<void> {
  const currentTypeEdges = relationships.filter(
    (r) => r.SubjectId === thingId && r.PredicateId === isPredicateId,
  );
  for (const edge of currentTypeEdges) {
    await relationshipApi.remove(edge.Id);
  }
  await relationshipApi.create(thingId, isPredicateId, newArchetypeId);
}

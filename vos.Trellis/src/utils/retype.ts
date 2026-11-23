import { relationshipApi } from '../api/relationshipApi';
import type { VosRelationship, VosThing } from '@tendril-ui/tendril';

// What a Thing can be retyped to. The platform refuses a Thing typed by one that was not declared a
// type, so nothing else is worth offering.
export function declaredTypes(things: VosThing[]): VosThing[] {
  return things.filter((thing) => thing.IsArchetype === true);
}

// Changes one of a Thing's types and keeps the rest, because a Thing can have several; without
// `fromArchetypeId` it replaces the only one. Correcting a wrongly classified element changes what derived
// properties and analysis pick up.
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

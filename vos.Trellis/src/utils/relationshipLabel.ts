import type { VosRelationship } from '../types/vos';
import { formatGuid } from './formatters';

/**
 * How a relationship reads: "subject predicate target".
 *
 * The platform sends `Name` only when it is something a client could not work out — an explicitly
 * set name, or a stored one left behind by a rename. Otherwise the name is generated from the three
 * endpoints, all of which are already in the payload, so composing it here saves sending the single
 * biggest repeated string in a model read (#6187).
 *
 * `nameOf` resolves a Thing id to its name; an id it cannot resolve reads as a shortened id, which is
 * what the panels already showed for an endpoint missing from the loaded model.
 */
export function relationshipLabel(
  relationship: VosRelationship,
  nameOf: (id: string) => string | undefined,
): string {
  if (relationship.Name) return relationship.Name;
  const part = (id: string) => nameOf(id) ?? formatGuid(id);
  return `${part(relationship.SubjectId)} ${part(relationship.PredicateId)} ${part(relationship.TargetId)}`;
}

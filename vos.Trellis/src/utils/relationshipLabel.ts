import type { VosRelationship } from '@tendril-ui/tendril';
import { formatGuid } from './formatters';

/**
 * The platform sends `Name` only for a relationship named when it was created; any other is composed here
 * from its endpoints, which saves sending the most repeated string in a model read and shows a renamed
 * endpoint at once. An identifier `nameOf` cannot resolve reads shortened, as the panels show an endpoint
 * missing from the loaded model.
 */
export function relationshipLabel(
  relationship: VosRelationship,
  nameOf: (id: string) => string | undefined,
): string {
  if (relationship.Name) return relationship.Name;
  const part = (id: string) => nameOf(id) ?? formatGuid(id);
  return `${part(relationship.SubjectId)} ${part(relationship.PredicateId)} ${part(relationship.TargetId)}`;
}

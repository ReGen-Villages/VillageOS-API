import type { VosRelationship, VosThing } from '../types/vos';
import { CONTAINMENT_ROLE_ID, thingBoundBy } from '../api/platformRoles';
import { IS_PREDICATE } from '../types/dashboard';

function containmentPredicateIds(things: readonly VosThing[], relationships: readonly VosRelationship[]): Set<string> {
  const kind = thingBoundBy(CONTAINMENT_ROLE_ID, things, relationships);
  if (!kind) return new Set();

  const isIds = new Set(things.filter((thing) => thing.Name === IS_PREDICATE).map((thing) => thing.Id));
  const isChildren = new Map<string, string[]>();
  for (const r of relationships) {
    if (!isIds.has(r.PredicateId)) continue;
    const children = isChildren.get(r.TargetId);
    if (children) children.push(r.SubjectId);
    else isChildren.set(r.TargetId, [r.SubjectId]);
  }

  const found = new Set<string>([kind.Id]);
  const frontier = [kind.Id];
  while (frontier.length) {
    for (const child of isChildren.get(frontier.pop()!) ?? []) {
      if (found.has(child)) continue;
      found.add(child);
      frontier.push(child);
    }
  }
  return found;
}

export function thingsContainedBy(
  container: VosThing,
  things: ReadonlyMap<string, VosThing>,
  relationships: readonly VosRelationship[],
): VosThing[] {
  const predicates = containmentPredicateIds([...things.values()], relationships);
  if (predicates.size === 0) return [];

  const contained: VosThing[] = [];
  for (const r of relationships) {
    if (r.SubjectId !== container.Id || !predicates.has(r.PredicateId)) continue;
    const child = things.get(r.TargetId);
    if (child) contained.push(child);
  }
  return contained;
}

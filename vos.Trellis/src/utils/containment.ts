import type { VosRelationship, VosThing } from '../types/vos';
import { CONTAINMENT_ROLE_ID } from '../api/platformRoles';
import { BINDS_PREDICATE_NAME } from '../api/landIntakeRoles';
import { IS_PREDICATE } from '../types/dashboard';

function idsNamed(name: string, things: ReadonlyMap<string, VosThing>): Set<string> {
  const ids = new Set<string>();
  for (const [id, thing] of things) if (thing.Name === name) ids.add(id);
  return ids;
}

function containmentPredicateIds(
  things: ReadonlyMap<string, VosThing>,
  relationships: readonly VosRelationship[],
): Set<string> {
  const bindsIds = idsNamed(BINDS_PREDICATE_NAME, things);
  const bound = relationships.find((r) => r.SubjectId === CONTAINMENT_ROLE_ID && bindsIds.has(r.PredicateId));
  if (!bound) return new Set();

  const isIds = idsNamed(IS_PREDICATE, things);
  const isChildren = new Map<string, string[]>();
  for (const r of relationships) {
    if (!isIds.has(r.PredicateId)) continue;
    const children = isChildren.get(r.TargetId);
    if (children) children.push(r.SubjectId);
    else isChildren.set(r.TargetId, [r.SubjectId]);
  }

  const found = new Set<string>([bound.TargetId]);
  const frontier = [bound.TargetId];
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
  const predicates = containmentPredicateIds(things, relationships);
  if (predicates.size === 0) return [];

  const contained: VosThing[] = [];
  for (const r of relationships) {
    if (r.SubjectId !== container.Id || !predicates.has(r.PredicateId)) continue;
    const child = things.get(r.TargetId);
    if (child) contained.push(child);
  }
  return contained;
}

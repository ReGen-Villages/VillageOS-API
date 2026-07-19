import type { VosThing, VosRelationship } from '../types/vos';

/** Return a new VosThing with one property merged/overwritten. */
export function applyThingPropertyUpdate(
  thing: VosThing,
  propertyPath: string,
  newValue: unknown,
): VosThing {
  return { ...thing, Properties: { ...thing.Properties, [propertyPath]: newValue } };
}

/** Return a new VosRelationship with one property merged/overwritten. */
export function applyRelationshipPropertyUpdate(
  rel: VosRelationship,
  propertyName: string,
  newValue: unknown,
): VosRelationship {
  return { ...rel, Properties: { ...rel.Properties, [propertyName]: newValue } };
}

/** Check if a relationship is connected to the given node (visible in its detail panel). */
export function isVisibleRelationship(
  relId: string,
  selectedNodeId: string | null,
  relationships: VosRelationship[],
): boolean {
  if (!selectedNodeId) return false;
  const rel = relationships.find((r) => r.Id === relId);
  if (!rel) return false;
  return rel.SubjectId === selectedNodeId || rel.TargetId === selectedNodeId;
}

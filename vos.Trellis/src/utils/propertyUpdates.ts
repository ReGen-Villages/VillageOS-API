import type { VosThing, VosRelationship } from '../types/vos';

/** Properties that affect graph rendering and require a full things array rebuild. */
const GRAPH_AFFECTING_PROPS = new Set(['geometry']);

/** Returns true if changing this property requires rebuilding the things array for graph rendering. */
export function isGraphAffectingProperty(propertyPath: string): boolean {
  return GRAPH_AFFECTING_PROPS.has(propertyPath);
}

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

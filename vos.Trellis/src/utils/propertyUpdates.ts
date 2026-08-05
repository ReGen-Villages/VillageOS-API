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

/** Return a new VosThing without the named property. */
export function applyThingPropertyRemoval(thing: VosThing, propertyPath: string): VosThing {
  if (!(propertyPath in thing.Properties)) return thing;
  const { [propertyPath]: _removed, ...remaining } = thing.Properties;
  return { ...thing, Properties: remaining };
}

/** Return a new VosRelationship without the named property. */
export function applyRelationshipPropertyRemoval(rel: VosRelationship, propertyName: string): VosRelationship {
  if (!(propertyName in rel.Properties)) return rel;
  const { [propertyName]: _removed, ...remaining } = rel.Properties;
  return { ...rel, Properties: remaining };
}

/**
 * Whether a relationship is on screen, so a live change to it is worth applying.
 *
 * Two ways it can be: it is the edge the user opened, or it hangs off the node they opened and
 * shows in that node's relationship list. Selecting an edge clears the node selection and the
 * reverse, so both have to be asked — a node-only test drops every change to the very edge whose
 * panel is in front of the user.
 */
export function isVisibleRelationship(
  relId: string,
  selectedNodeId: string | null,
  selectedEdgeId: string | null,
  relationships: VosRelationship[],
): boolean {
  if (selectedEdgeId && relId === selectedEdgeId) return true;
  if (!selectedNodeId) return false;
  const rel = relationships.find((r) => r.Id === relId);
  if (!rel) return false;
  return rel.SubjectId === selectedNodeId || rel.TargetId === selectedNodeId;
}

import type { InheritedPropertySet, VosThing, VosRelationship } from '@tendril-ui/tendril';

export function applyThingPropertyUpdate(
  thing: VosThing,
  propertyPath: string,
  newValue: unknown,
): VosThing {
  return { ...thing, Properties: { ...thing.Properties, [propertyPath]: newValue } };
}

export function applyRelationshipPropertyUpdate(
  relationship: VosRelationship,
  propertyName: string,
  newValue: unknown,
): VosRelationship {
  return { ...relationship, Properties: { ...relationship.Properties, [propertyName]: newValue } };
}

/** Both places are cleared because a live update files a value among own values even where the platform stores it in
 *  the override store. */
export function applyThingPropertyRemoval(thing: VosThing, propertyPath: string): VosThing {
  const overrides = withoutOverride(thing.InheritedOverrides, propertyPath);
  if (!(propertyPath in thing.Properties) && overrides === thing.InheritedOverrides) return thing;
  const { [propertyPath]: _removed, ...remaining } = thing.Properties;
  return { ...thing, Properties: remaining, ...(overrides && { InheritedOverrides: overrides }) };
}

/** A set left empty is dropped because the platform drops it, so a reload would not bring it back. */
function withoutOverride(
  sets: Record<string, InheritedPropertySet> | undefined,
  name: string,
): Record<string, InheritedPropertySet> | undefined {
  if (!sets) return sets;
  let changed = false;
  const kept: Record<string, InheritedPropertySet> = {};
  for (const [sourceId, set] of Object.entries(sets)) {
    const remaining = setWithout(set, name);
    if (remaining !== set) changed = true;
    if (remaining) kept[sourceId] = remaining;
  }
  return changed ? kept : sets;
}

function setWithout(set: InheritedPropertySet, name: string): InheritedPropertySet | null {
  const inherited = withoutOverride(set.Inherited, name);
  if (!(name in set.Properties) && inherited === set.Inherited) return set;
  const { [name]: _value, ...properties } = set.Properties;
  if (Object.keys(properties).length === 0 && Object.keys(inherited ?? {}).length === 0) return null;
  const remaining: InheritedPropertySet = { ...set, Properties: properties };
  if (set.PropertyWriteKinds) {
    const { [name]: _writeKind, ...writeKinds } = set.PropertyWriteKinds;
    remaining.PropertyWriteKinds = writeKinds;
  }
  if (inherited) remaining.Inherited = inherited;
  return remaining;
}

export function applyRelationshipPropertyRemoval(relationship: VosRelationship, propertyName: string): VosRelationship {
  if (!(propertyName in relationship.Properties)) return relationship;
  const { [propertyName]: _removed, ...remaining } = relationship.Properties;
  return { ...relationship, Properties: remaining };
}

/** On screen means opened itself, or listed under the opened node. Selecting a relationship clears the node
 *  selection and the other way round, so both are asked. */
export function isVisibleRelationship(
  relationshipId: string,
  selectedNodeId: string | null,
  selectedEdgeId: string | null,
  relationships: VosRelationship[],
): boolean {
  if (selectedEdgeId && relationshipId === selectedEdgeId) return true;
  if (!selectedNodeId) return false;
  const relationship = relationships.find((r) => r.Id === relationshipId);
  if (!relationship) return false;
  return relationship.SubjectId === selectedNodeId || relationship.TargetId === selectedNodeId;
}

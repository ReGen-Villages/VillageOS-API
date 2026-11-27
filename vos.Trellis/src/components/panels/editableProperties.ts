import { resolvedWriteKind, type EffectiveProperty, type IsChainLookup, type VosRelationship, type VosThing } from '@tendril-ui/tendril';

/** One property as this list shows it: what it is called, what it holds, and what the platform
 *  says it holds. The type comes from the platform's own reading of the property — a save states
 *  it rather than deriving it from how the typed text happens to look. */
export interface EditableProperty {
  name: string;
  value: unknown;
  type: string;
  takesReadingsOnly?: boolean;
}

/** Pair each property with the type the platform reports for it, dropping any the resolved set
 *  does not know — a property this list cannot name the type of is one it cannot save. */
export function withDeclaredTypes(
  properties: [string, unknown][],
  resolved: Record<string, EffectiveProperty> | null,
  takesReadingsOnly: (name: string) => boolean,
): EditableProperty[] {
  if (!resolved) return [];
  return properties
    .filter(([name]) => resolved[name])
    .map(([name, value]) => ({ name, value, type: resolved[name].Type, takesReadingsOnly: takesReadingsOnly(name) }));
}

export function thingTakesReadingsOnly(thing: VosThing, lookup: IsChainLookup): (name: string) => boolean {
  return (name) => resolvedWriteKind(thing, name, lookup) === 'ObservationOnly';
}

export function relationshipTakesReadingsOnly(relationship: VosRelationship): (name: string) => boolean {
  return (name) => relationship.PropertyWriteKinds?.[name] === 'ObservationOnly';
}

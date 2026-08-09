import type { EffectiveProperty, VosRelationship } from '../types/vos';
import { formatPropertyValue } from '../utils/formatters';
import { relationshipLabel } from '../utils/relationshipLabel';

export interface PropertyMatch {
  propertyName: string;
  value: unknown;
  /** What the platform says the property holds, for formatting the value (#6163). Absent on
   *  relationship matches, which are read from the model index and carry no declared type; those
   *  fall back to formatting by the value's own shape. */
  declaredType?: string;
  ownerType: 'thing' | 'relationship';
  ownerId: string;
  ownerName: string;
  /** For relationships: "subject --[predicate]--> target" */
  ownerDetail?: string;
  /** Non-null when the property is inherited — the source thing's name for the "via" badge. */
  inheritedFrom?: string;
}

/** Skip large blob properties that clutter results. */
const SKIP_KEYS = new Set(['geometry', 'footprint', '__geometry_envelope']);

export interface SearchInputs {
  /** Server-resolved effective properties per thing id (own + inherited); null while loading. Own
   *  properties are keyed by plain name, inherited by qualified path ("Home.energy_rating"). */
  effectiveProps: Record<string, Record<string, EffectiveProperty>> | null;
  relationships: VosRelationship[];
  thingNames: Map<string, string>;
  query: string;
  mode: 'name' | 'value';
}

/**
 * Find property matches across things (from the server-resolved effective set, so inherited values —
 * including archetype defaults the instance never overrode — are searchable) and relationships.
 */
export function searchProperties({ effectiveProps, relationships, thingNames, query, mode }: SearchInputs): PropertyMatch[] {
  const q = query.trim().toLowerCase();
  if (q.length === 0) return [];

  // Value search deliberately reads the stored value rather than how it is displayed: a date
  // matches the timestamp it is stored as, and a shape matches its coordinates rather than the one
  // word a cell has room for. Searching the presentation would find less, not more.
  const matchFn = mode === 'name'
    ? (key: string, _val: unknown) => key.toLowerCase().includes(q)
    : (_key: string, val: unknown) => formatPropertyValue(val).toLowerCase().includes(q);

  const matches: PropertyMatch[] = [];

  for (const [thingId, props] of Object.entries(effectiveProps ?? {})) {
    const ownerName = thingNames.get(thingId) ?? thingId.substring(0, 8);
    for (const [key, ep] of Object.entries(props)) {
      const name = ep.IsInherited ? key.substring(key.lastIndexOf('.') + 1) : key;
      if (SKIP_KEYS.has(name)) continue;
      if (matchFn(name, ep.Value)) {
        matches.push({
          propertyName: name,
          value: ep.Value,
          declaredType: ep.Type,
          ownerType: 'thing',
          ownerId: thingId,
          ownerName,
          inheritedFrom: ep.IsInherited ? thingNames.get(ep.InheritedFrom ?? '') : undefined,
        });
      }
    }
  }

  for (const rel of relationships) {
    for (const key of Object.keys(rel.Properties)) {
      if (SKIP_KEYS.has(key)) continue;
      if (matchFn(key, rel.Properties[key])) {
        const subj = thingNames.get(rel.SubjectId) ?? rel.SubjectId.substring(0, 8);
        const pred = thingNames.get(rel.PredicateId) ?? rel.PredicateId.substring(0, 8);
        const targ = thingNames.get(rel.TargetId) ?? rel.TargetId.substring(0, 8);
        matches.push({
          propertyName: key,
          value: rel.Properties[key],
          ownerType: 'relationship',
          ownerId: rel.Id,
          ownerName: relationshipLabel(rel, (id) => thingNames.get(id)),
          ownerDetail: `${subj} --[${pred}]--> ${targ}`,
        });
      }
    }
  }

  return matches;
}

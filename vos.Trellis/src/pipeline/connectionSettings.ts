import { effectiveProperties, valueOrigin, type EffectiveProperty } from '@tendril-ui/tendril';
import type { PipelineModel } from './model';

/** The platform keys an inherited property by the path of archetypes it came through, and refuses a name two
 *  archetypes declare, so such a name has no type to write with. */
export function declaredType(resolved: Record<string, EffectiveProperty>, name: string): string | null {
  if (name in resolved) return resolved[name].Type;
  const inherited = Object.entries(resolved).filter(([path]) => path.endsWith(`.${name}`)).map(([, property]) => property);
  return new Set(inherited.map((property) => property.InheritedFrom)).size === 1 ? inherited[0].Type : null;
}

export interface ConnectionSetting {
  name: string;
  value: unknown;
  setOnTheConnection: boolean;
  /** What applies once the connection states nothing; absent where no archetype declares the name. */
  inherited?: { value: unknown; from: string | null };
}

/** Every property a connection or an archetype in its `is` chain declares, as the connection reads it. */
export function connectionSettings(model: PipelineModel, connectionId: string): ConnectionSetting[] {
  const connection = model.thing(connectionId);
  if (!connection) return [];
  const lookup = model.isChainLookup(connectionId);
  const stated = model.stated(connectionId);
  const statingNothing = { ...connection, Properties: {}, InheritedOverrides: undefined };
  const inherited = effectiveProperties(statingNothing, lookup);
  return Object.entries(effectiveProperties(connection, lookup))
    .map(([name, value]) => ({
      name,
      value,
      setOnTheConnection: name in stated,
      ...(name in inherited ? { inherited: { value: inherited[name], from: valueOrigin(statingNothing, name, lookup).assumedFrom } } : {}),
    }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

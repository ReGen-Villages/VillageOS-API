import { effectiveProperties, valueOrigin } from '@tendril-ui/tendril';
import type { PipelineModel } from './model';

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

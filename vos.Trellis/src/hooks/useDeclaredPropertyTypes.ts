import { useEffect, useState } from 'react';
import { thingApi } from '../api/thingApi';
import type { EffectiveProperty } from '../types/vos';

/** Every Thing's resolved properties, keyed by Thing id then property name. */
export type DeclaredPropertyTypes = Record<string, Record<string, EffectiveProperty>>;

/**
 * The type the platform declares for each property, for surfaces that read the client model index
 * (#6163). That index carries values but no types, because it is built from the model itself rather
 * than from the resolved-properties routes, so a value there can only be formatted by its own shape.
 *
 * One read covers every Thing. `enabled` holds it back until something is actually going to show a
 * property — a model-wide read is not worth making for a surface nobody has opened.
 *
 * Null while the read is in flight. A caller should format by shape until it lands rather than
 * showing nothing: a badly formatted value is recoverable, a missing one is not.
 */
export function useDeclaredPropertyTypes(enabled: boolean): DeclaredPropertyTypes | null {
  const [types, setTypes] = useState<DeclaredPropertyTypes | null>(null);

  useEffect(() => {
    if (!enabled || types) return;
    let cancelled = false;
    thingApi.getAllProperties('effective').then(
      (data) => { if (!cancelled) setTypes(data); },
      () => { if (!cancelled) setTypes({}); },
    );
    return () => { cancelled = true; };
  }, [enabled, types]);

  return types;
}

import { useEffect, useState } from 'react';
import { thingApi } from '../api/thingApi';
import { useModelStore } from '../stores/modelStore';
import type { EffectiveProperty } from '../types/vos';

/** Every Thing's resolved properties, keyed by Thing id then property name. */
export type DeclaredPropertyTypes = Record<string, Record<string, EffectiveProperty>>;

/**
 * The type the platform declares for each property, for surfaces that read the client model index
 * (#6163). That index carries values but no types, because it is built from the model itself rather
 * than from the resolved-properties routes, so a value there can only be formatted by its own shape.
 *
 * One read covers every Thing. Pass `enabled: false` to hold it back until something is actually
 * going to show a property — a model-wide read is not worth making for a surface nobody has opened.
 *
 * Null while the read is in flight. A caller should format by shape until it lands rather than
 * showing nothing: a badly formatted value is recoverable, a missing one is not.
 */
export function useDeclaredPropertyTypes(enabled = true): DeclaredPropertyTypes | null {
  const [types, setTypes] = useState<DeclaredPropertyTypes | null>(null);
  // Switching model clears the store rather than remounting the app, so a read that never repeated
  // would keep describing the model that was open before. These ids belong to that model.
  const loaded = useModelStore((state) => state.loaded);

  // Dropped while rendering rather than in an effect, the way the property rows reset their draft:
  // React re-runs the component before painting, so the previous model's types are never shown.
  const [readFor, setReadFor] = useState(loaded);
  if (readFor !== loaded) {
    setReadFor(loaded);
    if (!loaded) setTypes(null);
  }

  useEffect(() => {
    if (!enabled || !loaded || types) return;
    let cancelled = false;
    thingApi.getAllProperties('effective').then(
      (data) => { if (!cancelled) setTypes(data); },
      () => { if (!cancelled) setTypes({}); },
    );
    return () => { cancelled = true; };
  }, [enabled, loaded, types]);

  return types;
}

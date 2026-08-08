import { useEffect, useRef, useState } from 'react';
import { thingApi } from '../api/thingApi';
import { useModelStore } from '../stores/modelStore';
import type { EffectiveProperty } from '../types/vos';

/** Resolved properties keyed by Thing id then property name. */
export type DeclaredPropertyTypes = Record<string, Record<string, EffectiveProperty>>;

/**
 * The platform's resolved view of a Thing's properties — each value with the type the platform
 * declares for it and where it was inherited from (#6163). The client model index carries values but
 * no types, because it is built from the model rather than from the resolved-properties routes.
 *
 * Pass the ids a surface is actually showing and only those are read. Only a surface that
 * genuinely works across the whole model — searching every property — should leave `ids` out, because
 * that reads the resolved properties of every Thing, which on a large model is more than the model
 * load itself. Ids already held are not read again, so paging through results does not re-read them.
 *
 * Pass `enabled: false` to hold the read back until something is going to show a property.
 *
 * Null until the first answer lands. A caller should format by shape until then rather than showing
 * nothing: a badly formatted value is recoverable, a missing one is not.
 */
export function useDeclaredPropertyTypes(
  enabled = true,
  ids?: readonly string[],
): DeclaredPropertyTypes | null {
  const [types, setTypes] = useState<DeclaredPropertyTypes | null>(null);
  // Switching model clears the store rather than remounting the app, so a read that never repeated
  // would keep describing the model that was open before. These ids belong to that model.
  const loaded = useModelStore((state) => state.loaded);
  const read = useRef<Set<string> | 'all'>(new Set());

  // Dropped while rendering rather than in an effect, the way the property rows reset their draft:
  // React re-runs the component before painting, so the previous model's types are never shown.
  const [readFor, setReadFor] = useState(loaded);
  if (readFor !== loaded) {
    setReadFor(loaded);
    if (!loaded) setTypes(null);
  }

  const wanted = ids === undefined ? undefined : [...ids].sort().join(',');

  useEffect(() => {
    // The record of what has been read is cleared here rather than beside setTypes above: a render
    // can be discarded and re-run, and bookkeeping dropped during one that never commits would
    // forget reads that did happen.
    if (!loaded) {
      read.current = new Set();
      return;
    }
    if (!enabled) return;

    const requested = wanted === undefined ? undefined : wanted.split(',').filter(Boolean);
    if (requested === undefined && read.current === 'all') return;

    const missing = requested === undefined
      ? undefined
      : requested.filter((id) => read.current === 'all' || !read.current.has(id));
    if (missing?.length === 0) return;

    let cancelled = false;
    thingApi.getAllProperties('effective', missing).then(
      (data) => {
        if (cancelled) return;
        const alreadyRead = read.current;
        if (missing === undefined) read.current = 'all';
        else if (alreadyRead !== 'all') missing.forEach((id) => alreadyRead.add(id));
        setTypes((prev) => ({ ...(prev ?? {}), ...data }));
      },
      () => { if (!cancelled) setTypes((prev) => prev ?? {}); },
    );
    return () => { cancelled = true; };
  }, [enabled, loaded, wanted]);

  return types;
}

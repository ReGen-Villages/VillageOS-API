import { useEffect, useRef, useState } from 'react';
import { thingApi } from '../api/thingApi';
import { useModelStore } from '../stores/modelStore';
import type { EffectiveProperty } from '@tendril-ui/tendril';

export type DeclaredPropertyTypes = Record<string, Record<string, EffectiveProperty>>;

/**
 * The model index carries values but no types, so the platform's resolved properties are read here. Pass
 * only the ids a page shows, because without `ids` every Thing is read, more than the model load on a
 * large model; ids already read are not read again. Null until the first answer, and a caller formats by
 * shape until then, because a badly formatted value is recoverable and a missing one is not.
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
    thingApi.getAllProperties(missing).then(
      (data) => {
        if (cancelled) return;
        const alreadyRead = read.current;
        if (missing === undefined) read.current = 'all';
        else if (alreadyRead !== 'all') missing.forEach((id) => alreadyRead.add(id));
        setTypes((previous) => ({ ...(previous ?? {}), ...data }));
      },
      () => { if (!cancelled) setTypes((previous) => previous ?? {}); },
    );
    return () => { cancelled = true; };
  }, [enabled, loaded, wanted]);

  return types;
}

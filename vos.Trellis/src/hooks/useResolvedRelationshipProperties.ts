import { useState, useEffect } from 'react';
import { relationshipApi } from '../api/relationshipApi';
import type { EffectiveProperty } from '../types/vos';

/**
 * A relationship's properties as the platform resolves them — own and inherited together, each
 * carrying its declared type and where it came from. The model store holds only the stored values,
 * so a panel that has to name a property's type has to ask.
 *
 * Null until the answer arrives, and again if the read fails: a caller shows what it has and offers
 * no editing rather than guessing a type. `version` re-reads after a write, since the resolved view
 * is what changes when an inherited value is overridden. `enabled` false asks nothing at all — a
 * panel that opens on another tab should not read properties nobody has looked at.
 */
export function useResolvedRelationshipProperties(
  relationshipId: string,
  { version = 0, enabled = true }: { version?: number; enabled?: boolean } = {},
): Record<string, EffectiveProperty> | null {
  const [resolved, setResolved] = useState<Record<string, EffectiveProperty> | null>(null);

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    (async () => {
      try {
        const properties = await relationshipApi.getEffectiveProperties(relationshipId);
        if (!cancelled) setResolved(properties);
      } catch {
        if (!cancelled) setResolved(null);
      }
    })();
    return () => { cancelled = true; };
  }, [relationshipId, version, enabled]);

  return resolved;
}

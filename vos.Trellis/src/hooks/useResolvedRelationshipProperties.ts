import { useState, useEffect } from 'react';
import { relationshipApi } from '../api/relationshipApi';
import type { EffectiveProperty } from '@tendril-ui/tendril';

/**
 * The model store holds only stored values, so a panel naming a property's type has to ask. Null until the
 * answer and after a failed read, so a caller offers no editing rather than guessing a type. `version` reads
 * again after a write, because overriding an inherited value changes the resolved view; `enabled` false asks
 * nothing, so a panel opening on another tab reads no properties nobody has looked at.
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

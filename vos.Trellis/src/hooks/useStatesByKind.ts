import { useCallback, useRef, useState } from 'react';
import type { ModelIndex } from '../api/dashboardApi';
import { rangeApi } from '../api/rangeApi';
import { statesOf } from '../api/modelDeclaration';

/**
 * The states each kind derives, read once per kind the first time it is asked for and kept with the
 * index they were read against. A kind asked about before its answer lands reads as deriving
 * nothing, and the asker redraws when the answer arrives.
 */
export function useStatesByKind(modelIndex: ModelIndex): (kind: string) => string[] | undefined {
  const known = useRef(new WeakMap<ModelIndex, Map<string, string[] | null>>());
  const [, redraw] = useState(0);
  return useCallback(
    (kind: string) => {
      let byKind = known.current.get(modelIndex);
      if (!byKind) {
        byKind = new Map();
        known.current.set(modelIndex, byKind);
      }
      const held = byKind.get(kind);
      if (held) return held;
      if (held === null) return undefined;
      const archetype = modelIndex.byName.get(kind);
      if (!archetype) return undefined;
      byKind.set(kind, null);
      rangeApi
        .getAll(archetype.Id)
        .then((ranges) => { byKind.set(kind, statesOf(ranges)); redraw((n) => n + 1); })
        .catch(() => { byKind.set(kind, []); redraw((n) => n + 1); });
      return undefined;
    },
    [modelIndex],
  );
}

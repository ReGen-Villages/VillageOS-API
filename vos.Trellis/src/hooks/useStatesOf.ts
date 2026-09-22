import { useEffect, useState } from 'react';
import type { ModelIndex } from '../api/dashboardApi';
import { rangeApi } from '../api/rangeApi';
import { statesOf } from '../api/modelDeclaration';

/** The states a kind derives, own and inherited, read once per chosen kind. The answer is kept
 *  with the kind it was read for, so a kind just chosen offers nothing until its own states land
 *  rather than the previous kind's. */
export function useStatesOf(kind: string, modelIndex: ModelIndex): string[] {
  const [read, setRead] = useState<{ kind: string; states: string[] }>({ kind: '', states: [] });
  useEffect(() => {
    const archetype = kind ? modelIndex.byName.get(kind) : undefined;
    if (!archetype) return;
    let current = true;
    rangeApi.getAll(archetype.Id)
      .then((ranges) => { if (current) setRead({ kind, states: statesOf(ranges) }); })
      .catch(() => { if (current) setRead({ kind, states: [] }); });
    return () => { current = false; };
  }, [kind, modelIndex]);
  return read.kind === kind ? read.states : [];
}

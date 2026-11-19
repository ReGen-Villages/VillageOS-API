import { useEffect, useState } from 'react';
import type { VosThing } from '@tendril-ui/tendril';
import { thingApi } from '../api/thingApi';

/** Read again whenever the held model changes, so a live edit reaches the panel showing the Thing. */
export function useSelectedThing(selectedNodeId: string | null, thingMap: Map<string, VosThing>): VosThing | null {
  const [fetched, setFetched] = useState<VosThing | null>(null);

  useEffect(() => {
    if (!selectedNodeId) return;
    let cancelled = false;
    thingApi.get(selectedNodeId).then(
      (thing) => { if (!cancelled) setFetched(thing); },
      () => { if (!cancelled) setFetched(thingMap.get(selectedNodeId) ?? null); },
    );
    return () => { cancelled = true; };
  }, [selectedNodeId, thingMap]);

  return selectedNodeId ? fetched : null;
}

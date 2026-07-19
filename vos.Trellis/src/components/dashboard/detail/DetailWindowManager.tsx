/**
 * Manages the set of open detail windows so several Things can be inspected at once.
 * Opening an already-open Thing brings its window to the front instead of duplicating it.
 * Stacking order == front-most last; z-index and cascade offset follow that order.
 */
import { useCallback, useState } from 'react';
import type { ModelIndex } from '../../../api/dashboardApi';
import type { DetailSpec } from '../../../types/dashboard';
import { EntityDetailWindow } from './EntityDetailWindow';

export function useDetailWindows(idx: ModelIndex, detail: DetailSpec | undefined, nonce?: number) {
  const [order, setOrder] = useState<string[]>([]);
  // Bumped when a window's "spread" button is clicked; every window re-tiles on the change.
  const [spreadTick, setSpreadTick] = useState(0);

  const openDetail = useCallback((thingId: string) => {
    if (!thingId) return;
    setOrder((prev) => [...prev.filter((id) => id !== thingId), thingId]);
  }, []);

  const spread = useCallback(() => setSpreadTick((tick) => tick + 1), []);

  const focus = useCallback((thingId: string) => {
    setOrder((prev) => (prev[prev.length - 1] === thingId ? prev : [...prev.filter((id) => id !== thingId), thingId]));
  }, []);

  const close = useCallback((thingId: string) => {
    setOrder((prev) => prev.filter((id) => id !== thingId));
  }, []);

  const windows = detail
    ? order.map((thingId, i) => (
        <EntityDetailWindow
          key={thingId}
          idx={idx}
          thingId={thingId}
          detail={detail}
          nonce={nonce}
          offset={i}
          index={i}
          total={order.length}
          spreadTick={spreadTick}
          zIndex={40 + i}
          onClose={() => close(thingId)}
          onFocus={() => focus(thingId)}
          onSpread={spread}
          openDetail={openDetail}
        />
      ))
    : null;

  return { openDetail: detail ? openDetail : undefined, windows };
}

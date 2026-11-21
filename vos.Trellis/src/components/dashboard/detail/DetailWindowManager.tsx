/** Opening a Thing already open brings its window to the front rather than opening a second. The front-most
 *  window is last, and the stacking and cascade follow that order. */
import { useCallback, useState } from 'react';
import type { ModelIndex } from '@tendril-ui/tendril';
import type { DetailSpecification } from '@tendril-ui/tendril';
import { EntityDetailWindow } from './EntityDetailWindow';
import { useDeclaredPropertyTypes } from '../../../hooks/useDeclaredPropertyTypes';
import { useAuthentication } from '../../../hooks/useAuthentication';
import { mayChangeData } from '../../../utils/mayChangeData';

export function useDetailWindows(modelIndex: ModelIndex, detail: DetailSpecification | undefined, nonce?: number) {
  const { role } = useAuthentication();
  const [order, setOrder] = useState<string[]>([]);
  const [spreadTick, setSpreadTick] = useState(0);

  const openDetail = useCallback((thingId: string) => {
    if (!thingId) return;
    setOrder((previous) => [...previous.filter((id) => id !== thingId), thingId]);
  }, []);

  const spread = useCallback(() => setSpreadTick((tick) => tick + 1), []);

  const focus = useCallback((thingId: string) => {
    setOrder((previous) => (previous[previous.length - 1] === thingId ? previous : [...previous.filter((id) => id !== thingId), thingId]));
  }, []);

  const close = useCallback((thingId: string) => {
    setOrder((previous) => previous.filter((id) => id !== thingId));
  }, []);

  // Read once for all windows, only once one is open, and only for the Things they show, so a dashboard nobody
  // clicked into reads nothing and one clicked twice does not read the whole model.
  const declaredTypes = useDeclaredPropertyTypes(order.length > 0, order);

  const windows = detail
    ? order.map((thingId, i) => (
        <EntityDetailWindow
          key={thingId}
          modelIndex={modelIndex}
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
          declaredTypes={declaredTypes}
          mayChangeData={mayChangeData(role)}
        />
      ))
    : null;

  return { openDetail: detail ? openDetail : undefined, windows };
}

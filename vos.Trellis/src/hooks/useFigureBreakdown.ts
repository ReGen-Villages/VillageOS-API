import { useEffect, useState } from 'react';
import type { Binding } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';
import { breakdownOf, type FigureBreakdown } from '../api/figureBreakdown';

/** Called from the panel a reader opened, so a page nobody opened gathers none of this. It reads again on
 *  each refresh, and the answer on screen stays until the next arrives, so a busy model moves the figures
 *  rather than blanking the table. */
export function useFigureBreakdown(
  binding: Binding,
  context: ResolveContext,
): { loading: boolean; breakdown: FigureBreakdown | null } {
  const key = JSON.stringify(binding);
  const [resolved, setResolved] = useState<{ key: string; breakdown: FigureBreakdown | null }>({
    key: '\u0000init',
    breakdown: null,
  });

  useEffect(() => {
    let alive = true;
    breakdownOf(binding, context).then(
      (breakdown) => alive && setResolved({ key, breakdown }),
      () => alive && setResolved({ key, breakdown: null }),
    );
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, context]);

  const matched = resolved.key === key;
  return { loading: !matched, breakdown: matched ? resolved.breakdown : null };
}

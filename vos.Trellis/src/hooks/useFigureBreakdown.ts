import { useEffect, useState } from 'react';
import type { Binding } from '../types/dashboard';
import type { ResolveContext } from '../api/dashboardApi';
import { breakdownOf, type FigureBreakdown } from '../api/figureBreakdown';

/** What a figure is made of. Called from the panel a reader opened, so a page nobody has clicked
 *  into never gathers any of this.
 *
 *  An open breakdown keeps reading live: the context's identity changes on every refresh the page
 *  makes, and the answer already on screen stays there until the next one lands, so a busy model
 *  moves the figures rather than blanking the table under them. */
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

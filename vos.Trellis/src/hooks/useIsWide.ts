import { useEffect, useState } from 'react';

/** The narrowest screen laid out as a desktop: below it, panels that sit beside a page start closed so
 *  the page keeps the width. */
export const TABLET_WIDTH = 768;

/** Whether the window is at least `minWidth` pixels wide, following it as it is resized. Where the
 *  browser cannot be asked, the answer is wide, which is the layout every page was first drawn for. */
export function useIsWide(minWidth: number): boolean {
  const supported = typeof window !== 'undefined' && typeof window.matchMedia === 'function';
  const [wide, setWide] = useState(() => (supported ? window.matchMedia(`(min-width:${minWidth}px)`).matches : true));
  useEffect(() => {
    if (!supported) return;
    const mediaQuery = window.matchMedia(`(min-width:${minWidth}px)`);
    const follow = () => setWide(mediaQuery.matches);
    mediaQuery.addEventListener('change', follow);
    return () => mediaQuery.removeEventListener('change', follow);
  }, [minWidth, supported]);
  return wide;
}

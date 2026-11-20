import { useEffect, useState } from 'react';

/** The full height, padding and border included, because a caller stacking rows needs it. Otherwise as
 *  `useElementWidth`. */
export function useElementHeight(): [(element: HTMLElement | null) => void, number] {
  const [element, setElement] = useState<HTMLElement | null>(null);
  const [height, setHeight] = useState(0);

  useEffect(() => {
    if (!element || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(([entry]) => setHeight(entry.target.getBoundingClientRect().height));
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);

  return [setElement, height];
}

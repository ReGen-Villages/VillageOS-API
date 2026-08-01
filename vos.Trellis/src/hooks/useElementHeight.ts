import { useEffect, useState } from 'react';

/** Measures the full height — content, padding and border — of whatever element the returned
 *  ref is attached to. Callback ref and fallback behave as in `useElementWidth`: the element
 *  may mount late, and the height stays 0 until layout and where ResizeObserver is unavailable. */
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

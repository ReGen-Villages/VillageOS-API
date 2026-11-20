import { useEffect, useState } from 'react';

/** The content width, without padding and border, because a chart draws inside the padding. A callback
 *  ref, because the element usually appears only once its data arrives, and an effect on a ref object runs
 *  once against null. 0 until the element is laid out, and where ResizeObserver is unavailable. */
export function useElementWidth(): [(element: HTMLElement | null) => void, number] {
  const [element, setElement] = useState<HTMLElement | null>(null);
  const [width, setWidth] = useState(0);

  useEffect(() => {
    if (!element || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver((entries) => setWidth(entries[0].contentRect.width));
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);

  return [setElement, width];
}

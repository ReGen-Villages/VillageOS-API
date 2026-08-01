import { useEffect, useState } from 'react';

/** Measures the content width — padding and border excluded — of whatever element the returned
 *  ref is attached to. Its sibling `useElementHeight` reports the full box instead, because a
 *  chart draws inside the padding while a row's height includes it.
 *
 *  The ref is a callback rather than a `useRef` object on purpose: the element a widget wants
 *  measured usually appears only once its data arrives, and an effect keyed on a ref object runs
 *  once against `null` and never again. A callback ref re-runs the measurement when the element
 *  itself changes, so a late-mounting chart is measured rather than left at its fallback width.
 *
 *  Width is 0 until the element is laid out, and where ResizeObserver is unavailable. */
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

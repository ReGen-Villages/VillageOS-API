import { useEffect, type RefObject } from 'react';

/** A drag across the page would otherwise select the text it passes over. */
export function usePreventsSelectionWhileDragging(dragging: RefObject<boolean>) {
  useEffect(() => {
    const prevent = (event: Event) => {
      if (dragging.current) event.preventDefault();
    };
    document.addEventListener('selectstart', prevent);
    return () => document.removeEventListener('selectstart', prevent);
  }, [dragging]);
}

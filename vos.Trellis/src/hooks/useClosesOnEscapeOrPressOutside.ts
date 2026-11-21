import { useEffect, type RefObject } from 'react';

export function useClosesOnEscapeOrPressOutside(
  open: boolean,
  element: RefObject<HTMLElement | null>,
  close: () => void,
): void {
  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (element.current && !element.current.contains(event.target as Node)) close();
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') close();
    };
    // React can run this effect while the press that opened the popup is still on its way to the document.
    const frame = requestAnimationFrame(() => document.addEventListener('pointerdown', onPointerDown));
    document.addEventListener('keydown', onKeyDown);
    return () => {
      cancelAnimationFrame(frame);
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open, element, close]);
}

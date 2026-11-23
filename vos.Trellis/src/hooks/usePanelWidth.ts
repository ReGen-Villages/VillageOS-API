import { useCallback, useRef, useState } from 'react';

/** `handleSide` lets one gesture, pulling the handle away from the canvas, mean the same on both sides of the
 *  screen. A width is not saved, because it is where someone put a panel while working, not a setting. */
export interface PanelWidthBounds {
  initial: number;
  min: number;
  max: number;
  /** Which side of the panel the handle sits on — the side the canvas is on. */
  handleSide: 'left' | 'right';
}

/** How much one arrow key moves the handle. Coarse enough to cross a panel in a few presses. */
const KEYBOARD_STEP = 24;

interface PanelWidth {
  width: number;
  isResizing: boolean;
  handleProperties: {
    onPointerDown: (event: React.PointerEvent) => void;
    onPointerMove: (event: React.PointerEvent) => void;
    onPointerUp: (event: React.PointerEvent) => void;
    onLostPointerCapture: () => void;
    onKeyDown: (event: React.KeyboardEvent) => void;
    role: 'separator';
    tabIndex: 0;
    'aria-orientation': 'vertical';
    'aria-valuenow': number;
    'aria-valuemin': number;
    'aria-valuemax': number;
  };
}

export function usePanelWidth({ initial, min, max, handleSide }: PanelWidthBounds): PanelWidth {
  const [width, setWidth] = useState(initial);
  const [isResizing, setIsResizing] = useState(false);
  // Measured from where the drag began, or the handle would jump by however far into it the person clicked.
  const from = useRef<{ pointerX: number; width: number } | null>(null);

  const held = useCallback((wanted: number) => Math.min(max, Math.max(min, wanted)), [min, max]);

  const onPointerDown = useCallback(
    (event: React.PointerEvent) => {
      event.preventDefault();
      from.current = { pointerX: event.clientX, width };
      setIsResizing(true);
      event.currentTarget.setPointerCapture?.(event.pointerId);
    },
    [width],
  );

  const onPointerMove = useCallback(
    (event: React.PointerEvent) => {
      const start = from.current;
      if (!start) return;
      const travelled = event.clientX - start.pointerX;
      setWidth(held(start.width + (handleSide === 'right' ? travelled : -travelled)));
    },
    [handleSide, held],
  );

  const letGo = useCallback(() => {
    from.current = null;
    setIsResizing(false);
  }, []);

  const onPointerUp = useCallback(
    (event: React.PointerEvent) => {
      letGo();
      event.currentTarget.releasePointerCapture?.(event.pointerId);
    },
    [letGo],
  );

  const onKeyDown = useCallback(
    (event: React.KeyboardEvent) => {
      const towards = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
      if (towards === 0) return;
      event.preventDefault();
      setWidth((standing) => held(standing + towards * KEYBOARD_STEP * (handleSide === 'right' ? 1 : -1)));
    },
    [handleSide, held],
  );

  return {
    width,
    isResizing,
    handleProperties: {
      onPointerDown,
      onPointerMove,
      onPointerUp,
      // A drag released off the window, or taken by the browser, sends no pointer-up, and the panel would
      // follow a pointer nobody is pressing.
      onLostPointerCapture: letGo,
      onKeyDown,
      role: 'separator',
      tabIndex: 0,
      'aria-orientation': 'vertical',
      'aria-valuenow': width,
      'aria-valuemin': min,
      'aria-valuemax': max,
    },
  };
}

import { useCallback, useRef, useState } from 'react';

/**
 * How wide a side panel stands, and the drag that changes it.
 *
 * A panel beside the canvas has one side facing the work. `handleSide` says which one, so the same
 * gesture — pull the handle away from the canvas to make room — reads the same on both sides of
 * the screen. Nothing here is written down: a width is where somebody has put a panel while they
 * are working, not a setting, and the page opens on the width it was designed for.
 */
export interface PanelWidthBounds {
  initial: number;
  min: number;
  max: number;
  /** Which side of the panel the handle sits on — the side the canvas is on. */
  handleSide: 'left' | 'right';
}

/** How much one arrow key moves the handle. Coarse enough to cross a panel in a few presses. */
const KEYBOARD_STEP = 24;

export interface PanelWidth {
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
  // Where the pointer was and how wide the panel was when the drag began. Held together so a
  // drag reads as one gesture: following the pointer's own position instead would jump the handle
  // to wherever the handle was grabbed, by however far in it the person clicked.
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
      // A drag that ends where no pointer-up reaches us — released off the window, or taken away
      // by the browser — would otherwise leave the panel following a pointer nobody is pressing.
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

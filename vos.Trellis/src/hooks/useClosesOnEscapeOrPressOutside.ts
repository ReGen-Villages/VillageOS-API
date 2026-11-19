import type { RefObject } from 'react';

export function useClosesOnEscapeOrPressOutside(
  _open: boolean,
  _element: RefObject<HTMLElement | null>,
  _close: () => void,
): void {}

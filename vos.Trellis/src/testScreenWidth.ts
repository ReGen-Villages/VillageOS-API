import { act } from '@testing-library/react';
import { vi } from 'vitest';

/** jsdom answers no media query, so every query answers `wide` until `resize`. Undone by `vi.unstubAllGlobals()`. */
export function stubScreenWidth(wide: boolean): { resize: (wide: boolean) => void } {
  const screen = { wide, listeners: [] as (() => void)[] };
  vi.stubGlobal('matchMedia', (media: string) => ({
    get matches() {
      return screen.wide;
    },
    media,
    addEventListener: (_event: string, listener: () => void) => screen.listeners.push(listener),
    removeEventListener: (_event: string, listener: () => void) => {
      screen.listeners = screen.listeners.filter((held) => held !== listener);
    },
  }));
  return {
    resize: (next) =>
      act(() => {
        screen.wide = next;
        screen.listeners.forEach((listener) => listener());
      }),
  };
}

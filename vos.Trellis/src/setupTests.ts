import '@testing-library/jest-dom/vitest';

// vitest 4.x + jsdom does not ship a usable `localStorage` by default. Modules
// that read it at import time (e.g. `useUiStore` reading the persisted detail-
// panel width) crash the whole test file before any test runs. Provide a
// minimal in-memory shim so any test that touches such modules just works.
// jsdom ships no ResizeObserver. Components that size themselves to their container observe one
// and fall back to a default width when it is absent, so a shim that never fires keeps them on
// that fallback instead of crashing the render.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

if (!globalThis.localStorage || typeof globalThis.localStorage.getItem !== 'function') {
  const store: Record<string, string> = {};
  Object.defineProperty(globalThis, 'localStorage', {
    value: {
      getItem: (key: string) => store[key] ?? null,
      setItem: (key: string, val: string) => { store[key] = val; },
      removeItem: (key: string) => { delete store[key]; },
      clear: () => { for (const k of Object.keys(store)) delete store[k]; },
      get length() { return Object.keys(store).length; },
      key: (i: number) => Object.keys(store)[i] ?? null,
    },
    writable: true,
    configurable: true,
  });
}

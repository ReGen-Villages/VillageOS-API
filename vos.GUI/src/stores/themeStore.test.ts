import { describe, it, expect, vi, beforeEach } from 'vitest';

// matchMedia must be polyfilled BEFORE the store module imports it (the
// initial `theme` value is computed at module load).
const mqlListeners = new Set<(e: MediaQueryListEvent) => void>();
let mockSystemDark = false;
function setupMatchMedia() {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    configurable: true,
    value: (query: string) => ({
      matches: query.includes('dark') ? mockSystemDark : false,
      media: query,
      onchange: null,
      addEventListener: (_: string, cb: (e: MediaQueryListEvent) => void) => mqlListeners.add(cb),
      removeEventListener: (_: string, cb: (e: MediaQueryListEvent) => void) => mqlListeners.delete(cb),
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => true,
    }),
  });
}

function emitSystemChange(dark: boolean) {
  mockSystemDark = dark;
  mqlListeners.forEach((cb) => cb({ matches: dark, media: '(prefers-color-scheme: dark)' } as MediaQueryListEvent));
}

describe('themeStore', () => {
  beforeEach(() => {
    localStorage.clear();
    mqlListeners.clear();
    mockSystemDark = false;
    setupMatchMedia();
    vi.resetModules();
  });

  it('seeds from prefers-color-scheme when no override is stored', async () => {
    mockSystemDark = true;
    const { useThemeStore } = await import('./themeStore');
    expect(useThemeStore.getState().theme).toBe('dark');
    expect(useThemeStore.getState().isOverride).toBe(false);
  });

  it('uses the stored override over the system preference', async () => {
    mockSystemDark = true; // OS says dark
    localStorage.setItem('vos-theme-override', 'light'); // user previously chose light
    const { useThemeStore } = await import('./themeStore');
    expect(useThemeStore.getState().theme).toBe('light');
    expect(useThemeStore.getState().isOverride).toBe(true);
  });

  it('toggle flips the theme, marks it as override, and persists', async () => {
    mockSystemDark = false;
    const { useThemeStore } = await import('./themeStore');
    expect(useThemeStore.getState().theme).toBe('light');
    useThemeStore.getState().toggle();
    expect(useThemeStore.getState().theme).toBe('dark');
    expect(useThemeStore.getState().isOverride).toBe(true);
    expect(localStorage.getItem('vos-theme-override')).toBe('dark');
  });

  it('follows OS changes when the user has not overridden', async () => {
    mockSystemDark = false;
    const { useThemeStore, attachThemeMediaListener } = await import('./themeStore');
    const detach = attachThemeMediaListener();
    expect(useThemeStore.getState().theme).toBe('light');

    emitSystemChange(true);
    expect(useThemeStore.getState().theme).toBe('dark');

    emitSystemChange(false);
    expect(useThemeStore.getState().theme).toBe('light');
    detach();
  });

  it('ignores OS changes once the user has overridden', async () => {
    mockSystemDark = false;
    const { useThemeStore, attachThemeMediaListener } = await import('./themeStore');
    const detach = attachThemeMediaListener();
    useThemeStore.getState().toggle(); // user chose dark
    expect(useThemeStore.getState().theme).toBe('dark');

    emitSystemChange(false); // OS goes light — should NOT flip the user out of dark
    expect(useThemeStore.getState().theme).toBe('dark');
    detach();
  });

  it('followSystem clears the override and re-seeds from the OS', async () => {
    mockSystemDark = true;
    localStorage.setItem('vos-theme-override', 'light');
    const { useThemeStore } = await import('./themeStore');
    expect(useThemeStore.getState().theme).toBe('light');
    useThemeStore.getState().followSystem();
    expect(useThemeStore.getState().theme).toBe('dark');
    expect(useThemeStore.getState().isOverride).toBe(false);
    expect(localStorage.getItem('vos-theme-override')).toBeNull();
  });
});

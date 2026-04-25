import { create } from 'zustand';

const THEME_KEY = 'vos-theme-override';
const DARK_QUERY = '(prefers-color-scheme: dark)';

export type Theme = 'light' | 'dark';

interface ThemeState {
  /** The currently-applied theme. */
  theme: Theme;
  /** True if the user has explicitly chosen a theme; false means we're following the OS. */
  isOverride: boolean;
  /** Flip to the opposite theme. Marks the choice as a manual override. */
  toggle: () => void;
  /** Drop the override and follow the OS again (not used yet, but kept for symmetry). */
  followSystem: () => void;
  /** Internal: called by the matchMedia listener when the OS theme changes. */
  _systemChanged: (next: Theme) => void;
}

function systemPreference(): Theme {
  if (typeof window === 'undefined' || !window.matchMedia) return 'light';
  return window.matchMedia(DARK_QUERY).matches ? 'dark' : 'light';
}

function loadOverride(): Theme | null {
  if (typeof localStorage === 'undefined') return null;
  const v = localStorage.getItem(THEME_KEY);
  return v === 'light' || v === 'dark' ? v : null;
}

const initialOverride = loadOverride();
const initialTheme: Theme = initialOverride ?? systemPreference();

export const useThemeStore = create<ThemeState>((set, get) => ({
  theme: initialTheme,
  isOverride: initialOverride !== null,

  toggle: () => {
    const next: Theme = get().theme === 'dark' ? 'light' : 'dark';
    localStorage.setItem(THEME_KEY, next);
    set({ theme: next, isOverride: true });
  },

  followSystem: () => {
    localStorage.removeItem(THEME_KEY);
    set({ theme: systemPreference(), isOverride: false });
  },

  _systemChanged: (next) => {
    // Only follow OS changes when the user hasn't taken manual control.
    if (!get().isOverride) set({ theme: next });
  },
}));

/**
 * Subscribe to the OS theme so we live-update while the user is in
 * "follow system" mode. Idempotent — safe to call once at app boot.
 */
export function attachThemeMediaListener(): () => void {
  if (typeof window === 'undefined' || !window.matchMedia) return () => {};
  const mql = window.matchMedia(DARK_QUERY);
  const handler = (e: MediaQueryListEvent) => {
    useThemeStore.getState()._systemChanged(e.matches ? 'dark' : 'light');
  };
  mql.addEventListener('change', handler);
  return () => mql.removeEventListener('change', handler);
}

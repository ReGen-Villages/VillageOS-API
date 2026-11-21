import { create } from 'zustand';

const THEME_KEY = 'vos-theme-override';
const DARK_QUERY = '(prefers-color-scheme: dark)';

type Theme = 'light' | 'dark';

interface ThemeState {
  theme: Theme;
  /** False while the theme follows the operating system. */
  isOverride: boolean;
  toggle: () => void;
  /** Called only by the listener on the operating system's theme. */
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

  _systemChanged: (next) => {
    if (!get().isOverride) set({ theme: next });
  },
}));

/** Call once, when the application starts. */
export function attachThemeMediaListener(): () => void {
  if (typeof window === 'undefined' || !window.matchMedia) return () => {};
  const mql = window.matchMedia(DARK_QUERY);
  const handler = (e: MediaQueryListEvent) => {
    useThemeStore.getState()._systemChanged(e.matches ? 'dark' : 'light');
  };
  mql.addEventListener('change', handler);
  return () => mql.removeEventListener('change', handler);
}

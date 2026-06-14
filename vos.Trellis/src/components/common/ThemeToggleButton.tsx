import { Moon, Sun } from 'lucide-react';
import { useThemeStore } from '../../stores/themeStore';

/**
 * Toggle between light and dark theme. The icon previews the *target* mode —
 * a Sun when dark is active (click to switch to light), a Moon when light
 * is active. First click also flips the user out of "follow system" mode.
 */
export function ThemeToggleButton() {
  const theme = useThemeStore((s) => s.theme);
  const toggle = useThemeStore((s) => s.toggle);
  const dark = theme === 'dark';
  const next = dark ? 'light' : 'dark';

  return (
    <button
      type="button"
      onClick={toggle}
      title={`Switch to ${next} mode`}
      aria-label={`Switch to ${next} mode`}
      data-testid="theme-toggle"
      className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
    >
      {dark ? <Sun size={14} /> : <Moon size={14} />}
    </button>
  );
}

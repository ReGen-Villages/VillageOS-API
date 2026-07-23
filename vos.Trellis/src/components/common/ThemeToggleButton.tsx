import { Moon, Sun } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useThemeStore } from '../../stores/themeStore';

/**
 * Toggle between light and dark theme. The icon previews the *target* mode —
 * a Sun when dark is active (click to switch to light), a Moon when light
 * is active. First click also flips the user out of "follow system" mode.
 */
export function ThemeToggleButton() {
  const theme = useThemeStore((s) => s.theme);
  const toggle = useThemeStore((s) => s.toggle);
  const { t } = useTranslation();
  const dark = theme === 'dark';
  const targetMode = t(dark ? 'theme.light' : 'theme.dark');
  const switchLabel = t('theme.switchTo', { mode: targetMode });

  return (
    <button
      type="button"
      onClick={toggle}
      title={switchLabel}
      aria-label={switchLabel}
      data-testid="theme-toggle"
      className="flex items-center justify-center rounded p-1.5 text-zinc-600 hover:bg-zinc-200 dark:text-zinc-300 dark:hover:bg-zinc-800 transition-colors"
    >
      {dark ? <Sun size={16} /> : <Moon size={16} />}
    </button>
  );
}

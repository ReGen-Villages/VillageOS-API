import { Moon, Sun } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useThemeStore } from '../../stores/themeStore';

/** The icon shows the theme a click switches to. The first click also stops following the system theme. */
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
